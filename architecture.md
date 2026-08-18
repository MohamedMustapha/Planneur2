# Architecture

## 1. Big picture

```
┌────────────────────────────────────────────────────────────────────┐
│  Browser — Angular 22 (Signals, standalone, Mobiscroll, Transloco)  │
└───────────────▲──────────────────────────────────┬─────────────────┘
                │ cookie (HttpOnly, SameSite=Strict)│ same-origin /api
                │                                    ▼
┌────────────────────────────────────────────────────────────────────┐
│  BFF — YARP reverse proxy + OIDC (Keycloak)                          │
│  • Holds tokens server-side (no token in browser)                   │
│  • Adds access token to proxied calls                               │
│  • CSRF, anti-forgery, session cookie                               │
└───────────────────────────────┬────────────────────────────────────┘
                                 │ Bearer (internal)
                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│  Modular Monolith (.NET 10, ASP.NET Core)                           │
│  Host  ├─ Mediator (hand-written) + pipeline behaviors              │
│        ├─ Module: Directory      (2-layer + LDAP sync)              │
│        ├─ Module: Access         (infra / RLS context)             │
│        ├─ Module: Projects       (DDD)                              │
│        ├─ Module: Portfolio      (DDD)                              │
│        ├─ Module: Activities     (DDD)                              │
│        ├─ Module: Scheduling     (mixed — Mobiscroll read/assign)   │
│        ├─ Module: Meetings       (2-layer)                          │
│        ├─ Module: Reporting      (DDD orchestration + AI)           │
│        ├─ Module: Kudos          (DDD)                              │
│        ├─ Module: Integrations   (2-layer sync)                     │
│        └─ Module: Finance        (2-layer query — capex/opex)       │
└───┬───────────────┬───────────────┬───────────────┬────────────────┘
    │               │               │               │
    ▼               ▼               ▼               ▼
Postgres(RLS)   RustFS(S3)     On-prem LLM     Azure DevOps / ServiceNow
                                (OpenAI API)    (read-only pull)
    │
Observability: SEQ (logs) · Prometheus/Grafana (metrics) · OpenTelemetry traces
Auth source: Keycloak realm ⟵ LDAP sync (people, units via `fonction`, groups)
```

## 2. Solution layout

```
/src
  /Bff
    Bff.csproj                     # YARP + OIDC + anti-forgery
  /Host
    Host.csproj                    # composition root; wires every module
  /BuildingBlocks
    Mediator/                      # hand-written mediator (see conventions)
    Web/                           # FastEndpoints base, ProblemDetails, RLS filter
    Persistence/                   # DbContext base, RLS session interceptor, outbox
    Messaging/                     # in-process domain-event dispatch
    Observability/                 # SEQ + OTel + Prometheus meters
    Ai/                            # OpenAI-compatible client (on-prem)
    Storage/                       # RustFS/S3 client
    Testing/                       # shared test fixtures
  /Modules
    /Directory        (2-layer)
    /Access           (infra)
    /Projects         (DDD)   -> Domain / Application / Infrastructure / Api
    /Portfolio        (DDD)
    /Activities       (DDD)
    /Scheduling       (mixed)
    /Meetings         (2-layer)
    /Reporting        (DDD)
    /Kudos            (DDD)
    /Integrations     (2-layer)
    /Finance          (2-layer)
/tests
  /Unit /Integration /Architecture /E2E(Playwright)
/web
  angular workspace (see §7)
/deploy
  docker-compose.yml, keycloak/, grafana/, prometheus/, seq/
```

**Module isolation rule:** a module exposes only (a) its FastEndpoints and (b) a thin **public contracts** assembly (`Modules.Xxx.Contracts`) containing integration events and read-DTOs other modules may consume. No module references another module's Domain/Infrastructure. Cross-module reads go through contracts or a query sent on the mediator; cross-module writes go through **integration events** (in-process, via the outbox).

## 3. Database strategy

- **One Postgres database, one schema per module** (`directory`, `projects`, …). Foreign keys never cross schemas; cross-module references store the id only and are validated in the application layer.
- **RLS is the primary access-control mechanism.** Every row-bearing table that contains user/unit/department/project-scoped data has RLS enabled with `FORCE ROW LEVEL SECURITY`. Policies read Postgres session GUCs set per request (see `visibility-matrix.md`).
- The app connects as a **non-owner role** (`app_rw`) so `FORCE RLS` applies even to the writer. Migrations run as the owner role.
- **Outbox table** per module for reliable in-process integration events; a hosted service drains it.
- No ORM-level global filters for tenancy — RLS is the source of truth. EF query filters are used only for soft-delete convenience.

## 4. Authentication & the RLS session context

1. User authenticates at the **BFF** against **Keycloak** (Authorization Code + PKCE). BFF stores tokens; browser holds only an HttpOnly cookie.
2. BFF proxies to the API with the access token. The token carries: `sub`, `preferred_username`, LDAP-derived group claims, functional role, unit and department ids.
3. A **request middleware** in `BuildingBlocks/Web` builds an `IUserContext` (user id, unit id, department id(s), contextual roles) from claims, cross-checked against the `Access` module's RBAC fallback view.
4. A **DbConnection interceptor** in `BuildingBlocks/Persistence` issues, on every opened connection inside a request:
   ```sql
   SELECT set_config('app.user_id',   @userId,   true);
   SELECT set_config('app.unit_id',   @unitId,   true);
   SELECT set_config('app.dept_ids',  @deptCsv,  true);
   SELECT set_config('app.roles',     @rolesCsv, true);
   ```
   (`true` = local to the transaction.) RLS policies then read these via helper SQL functions.
5. Background jobs (sync, AI, outbox) run under a **service context** that sets `app.roles = 'system'`, which RLS policies treat as bypass-for-read where explicitly allowed.

## 5. Cross-cutting concerns (all in `BuildingBlocks`)

| Concern | Approach |
|---|---|
| Validation | FluentValidation, invoked by a mediator `ValidationBehavior` (DDD) or endpoint pre-processor (2-layer). |
| Errors | RFC 7807 `ProblemDetails`; domain errors → typed result, mapped centrally. Never leak exceptions to the client. |
| Transactions | `TransactionBehavior` opens a transaction per command; the RLS GUCs are set inside it. |
| Auditing | `created_by/at`, `modified_by/at` columns + append-only audit log for state changes on Projects, Portfolio, Kudos. |
| Idempotency | Integration-event consumers keyed by event id; sync jobs upsert by external id. |
| Time | All timestamps `timestamptz`, stored UTC. Display in user's timezone (from profile, default org tz). |
| Config | Per-department configuration lives in the `Directory`/`Access` modules and is cached with a signal on the client. |

## 6. Observability

- **Logs → SEQ** via Serilog, structured, enriched with `user_id`, `department_id`, `correlation_id`.
- **Metrics → Prometheus**, scraped from a `/metrics` endpoint; dashboards in Grafana. Custom meters: activity logging rate, sync latency/failures, AI summary duration/tokens, kudos per department, RLS-denied query count.
- **Traces → OpenTelemetry** (OTLP) — spans across BFF → API → DB → external integrations.
- Health checks: Postgres, RustFS, Keycloak, LLM endpoint, each integration.

## 7. Angular workspace (Angular 22)

- Standalone components, **Signals** everywhere; server state via `httpResource`/`resource` (no NgRx, no NgXs). Local component state via `signal`/`computed`; shared cross-view state via a handful of **injectable signal stores** (plain services exposing signals) — this is *not* a state-management library, it's the framework primitive.
- **Feature libraries** mirror the slices: `directory`, `projects`, `portfolio`, `activities`, `scheduling`, `meetings`, `reporting`, `kudos`, `finance`, plus `shared/ui`, `shared/auth`, `shared/i18n`.
- **Mobiscroll Angular** wraps the three timeline archetypes in `scheduling` (see S6). One licensed install; timelines configured per board.
- **i18n via Transloco** (runtime language switch fr/en/es without reload). All strings keyed; no literals in templates.
- Routing guards read the contextual role from the BFF session endpoint; the UI never trusts client-side role for data — RLS is authoritative.

## 8. Environments & deploy

- `docker-compose` for local: postgres, keycloak (+ LDAP), rustfs, seq, prometheus, grafana, the monolith, the BFF, and a stub OpenAI-compatible server for tests.
- One container for the monolith, one for the BFF, Angular served as static assets behind the BFF (same origin — no CORS).
- Secrets via environment / a vault; never in source.
