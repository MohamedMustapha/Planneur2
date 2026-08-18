# S0 — Platform Foundation

## Purpose
Stand up the empty-but-complete skeleton every later slice plugs into: the modular-monolith host, the hand-written mediator, EF Core + Postgres with the RLS session plumbing, the BFF + Keycloak auth flow, RustFS storage, the on-prem LLM client, observability, and the four-layer test harness. **No business features** — this slice is "hello world, secured, observable, tested."

## Depends on
Nothing.

## Module archetype
Infrastructure (`BuildingBlocks` + `Host` + `Bff`).

## Deliverables

### Mediator (`BuildingBlocks/Mediator`)
Implements the contracts in `conventions.md §1`: `ISender/IPublisher/IMediator`, `IRequestHandler`, `INotificationHandler`, `IPipelineBehavior`, `Unit`. Pipeline delegate compiled + cached per closed request type. Ships `LoggingBehavior`. Registrar extension `AddMediator(assembly)` per module.

### Web building blocks (`BuildingBlocks/Web`)
- FastEndpoints base config, global `ProblemDetails` mapper, correlation-id middleware.
- `IUserContext` + middleware that builds it from Keycloak claims (§4 architecture).
- Authorization policies for the five contextual roles (empty scopes for now).

### Persistence (`BuildingBlocks/Persistence`)
- `ModuleDbContext` base (schema-per-module).
- **RLS session interceptor**: on connection open inside a request, sets `app.user_id/unit_id/dept_ids/roles` GUCs from `IUserContext`.
- Roles: `app_owner` (migrations) and `app_rw` (runtime, non-owner so `FORCE RLS` bites).
- Outbox table + draining hosted service.
- `access` schema with the GUC accessor + `has/is_head/roles` helper functions from the matrix.

### Auth (`Bff`)
- YARP proxy to the monolith; OIDC (Keycloak, Auth Code + PKCE); tokens server-side; HttpOnly cookie; anti-forgery/CSRF; `/bff/user` endpoint returning the resolved context (id, roles, unit, dept, language).
- Keycloak realm export committed under `/deploy/keycloak` with an LDAP federation config and group→role mappers (dev seed uses an embedded LDAP with fixtures).

### Storage / AI / Observability
- `Storage`: S3 client pointed at RustFS; `PutObject/GetObject/presign`.
- `Ai`: OpenAI-compatible client (base URL + key from config), `ChatComplete(prompt, lang)`; a **stub server** for tests.
- `Observability`: Serilog→SEQ, OpenTelemetry OTLP traces, Prometheus meters + `/metrics`. Health checks for pg/rustfs/keycloak/llm.

### Test harness (`BuildingBlocks/Testing` + `/tests`)
- `IntegrationTestFixture`: spins Postgres via Testcontainers, applies migrations, exposes a `WebApplicationFactory` with pluggable `IUserContext` so tests can "become" any role.
- Architecture test project with the baseline rules (no MediatR, module isolation shell).
- Playwright project with a login helper against the seeded Keycloak.

## API surface
- `GET /health`, `GET /metrics`, `GET /bff/user`. One trivial secured `GET /api/ping` proving the full chain (cookie → BFF → API → RLS context set → response) works.

## RLS
No business tables yet, but the `access` schema, GUC accessors, and the interceptor must be proven: an integration test sets a fake context and asserts `select access.uid()` returns it.

## Angular surface
- Angular 22 workspace; standalone `AppComponent`; Transloco with fr/en/es dictionaries (login + shell strings); Mobiscroll installed and licensed (a smoke component renders an empty timeline).
- Auth: calls `/bff/user`, stores context in an injectable **signal store**; a language switcher writes to Transloco.
- A shell with left-nav placeholders for the slices.

## Tests
- **Unit:** mediator pipeline ordering + caching; ProblemDetails mapping.
- **Integration:** `/api/ping` returns 200 only when authenticated; GUCs set correctly; outbox drains.
- **Architecture:** baseline rules compile and pass on the empty modules.
- **E2E:** log in through Keycloak, land on the shell, switch language, see the empty timeline render.

## Acceptance criteria
- `docker-compose up` brings the whole stack up healthy.
- A user can log in via Keycloak (LDAP-backed), reach a secured endpoint, and their RLS context is provably set.
- All four test layers run in CI and pass.
- Grafana shows the stack's baseline metrics; SEQ shows structured logs with correlation ids.

## Out of scope
Any business entity. Directory sync (S1). Real role scopes (S2).
