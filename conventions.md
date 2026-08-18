# Conventions (binding for every slice)

## 1. The hand-written mediator

We do **not** use MediatR. `BuildingBlocks/Mediator` provides a minimal, source-friendly equivalent.

### Contracts
```csharp
public interface IRequest<out TResponse> { }
public interface IRequest : IRequest<Unit> { }          // command with no payload result
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

public interface INotification { }                       // in-process domain/integration event
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken ct);
}

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

public interface ISender  { Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default); }
public interface IPublisher { Task Publish(INotification notification, CancellationToken ct = default); }
public interface IMediator : ISender, IPublisher { }
```

### Implementation requirements
- Handlers and behaviors are resolved from DI. Registration is done **per module** via an `AddXxxModule()` extension that scans that module's assembly (`Scrutor` or a simple reflection registrar). No global assembly scanning across modules.
- `Send` builds the behavior pipeline for the closed request type once and **caches the delegate** (compiled expression or `ConcurrentDictionary<Type, Func<...>>`). No reflection on the hot path after warm-up.
- `Publish` fans out to all `INotificationHandler<T>`; default strategy = **sequential, stop on first exception** for domain events inside a transaction; a `ParallelWhenAll` strategy is available for post-commit fire-and-forget.
- **Behaviors run in registration order:** `Logging → Validation → Authorization → Transaction → Handler`. Only DDD modules use the full pipeline; 2-layer modules use `Logging → Handler` (validation happens at the endpoint).
- `Unit` is a struct with a single `Value` instance (mirrors MediatR's `Unit`).

### Guardrails
- Architecture tests assert: no reference to `MediatR.*`; every `IRequest` has exactly one handler; behaviors are stateless.

## 2. The two module archetypes

### 2-layer CRUD module  (Directory, Meetings, Integrations, Finance)
Use when the module is essentially data in/out with simple, local invariants.

```
Modules/<Name>/
  Endpoints/          # FastEndpoints — validation, mapping, auth attribute
  Services/           # application service(s): business calls + EF Core
  Data/               # DbContext, entity configs, migrations
  Contracts/          # integration events + read DTOs exposed to other modules
```
- Endpoint calls **service directly** (services are also invokable via the mediator when another module needs them, but the primary path is endpoint→service).
- No aggregates, no domain events unless a genuine cross-module fact must be published.
- Validation with FluentValidation in a FastEndpoints pre-processor.

### Clean Architecture DDD module  (Projects, Portfolio, Activities, Reporting, Kudos)
Use when there is a real domain: state machines, invariants spanning several entities, domain events.

```
Modules/<Name>/
  Domain/             # aggregates, entities, value objects, domain events, domain services
  Application/        # commands, queries, handlers, validators, port interfaces
  Infrastructure/     # EF configs, repositories, adapters (LLM, storage, external)
  Api/                # FastEndpoints -> send commands/queries via mediator
  Contracts/          # integration events + read DTOs exposed to other modules
```
- Endpoints are thin: map request → command/query, `Send`, map result.
- Repositories return aggregates; queries may bypass repositories and read projections directly for performance.
- Domain events are raised inside aggregates, dispatched after save; those that must cross a module boundary are converted to **integration events** written to the module outbox.

**Decision rule:** if you can describe the module as "tables + forms + a couple of rules," it's 2-layer. If it has a lifecycle diagram, it's DDD.

## 3. API conventions

- **FastEndpoints** for all HTTP. One endpoint = one file. Route pattern: `/api/<module>/<resource>`.
- Verbs: standard REST; long-running/async pulls (integrations, AI) return `202` + a status resource.
- Every endpoint declares its **required contextual role** via an authorization policy; **but data filtering is RLS**, never a `WHERE` on role in application code. Endpoints fail closed.
- Pagination: cursor-based (`?after=&limit=`) for lists that back timelines; offset allowed for admin tables.
- All responses camelCase JSON; errors are `ProblemDetails`.

## 4. Persistence & RLS conventions

- Every scoped table has: `owner_id`, and where relevant `unit_id`, `department_id`, `project_id`. These feed RLS predicates.
- RLS predicates are centralized as SQL functions in the `access` schema (e.g. `access.can_read_activity(...)`) so policies stay one-liners and the matrix lives in one place.
- Migrations: EF Core migrations per module schema; a migration also creates/updates the RLS policies for that module's tables (raw SQL in the migration).
- Never disable RLS in application code. Tests that need to see all rows connect as the owner role explicitly and document why.

## 5. i18n (fr / en / es)

- **Transloco**, JSON dictionaries per language per feature lib. Key format `feature.screen.element` (e.g. `activities.board.logButton`).
- Server-originated user-facing text (report section titles, notification templates) is **also keyed**; the API returns keys + params, the client renders them. The AI summary is generated in the user's active UI language (the prompt states the language).
- Enums that appear in UI (activity types, lifecycle states) are keyed; the DB stores stable codes, never localized text.
- Dates/numbers via Angular locale; default org language configurable, per-user override.

## 6. Testing strategy (required for every slice)

| Layer | Tooling | What it covers |
|---|---|---|
| **Unit** | xUnit + FluentAssertions | Domain aggregates, services, mediator behaviors, validators. Pure, no I/O. |
| **Integration** | `WebApplicationFactory` + **Testcontainers (Postgres)** | Endpoint→service/handler→DB, **including RLS**: run the same query as different user contexts and assert row visibility. 2-layer modules are covered end-to-end here (they have no domain unit tests to speak of). |
| **Architecture** | NetArchTest / custom | Module isolation (no cross-module Domain refs), no `MediatR`, DDD layer dependencies (Domain refs nothing), endpoints don't touch DbContext directly in DDD modules. |
| **E2E** | **Playwright** | Critical user journeys per slice through the real BFF+Angular against a seeded stack. Multi-role: log in as member / lead / head and assert what each can see. |

**RLS is tested as a first-class concern.** Each data slice ships a matrix test: seed a fixed org (2 departments, 2 units each, N people, cross-dept project) and assert, for every contextual role, the exact set of visible rows. This is the executable form of `visibility-matrix.md`.

## 7. Definition of Done (per slice)

- All four test layers present and green; RLS matrix test green.
- Endpoints documented (OpenAPI) and behind the correct auth policy.
- i18n keys present for fr/en/es.
- Grafana metrics/counters emitted where the slice notes them.
- Migrations idempotent; `docker-compose up` seeds a working demo of the slice.
- No literals, no MediatR, no cross-module Domain references (arch tests prove it).
