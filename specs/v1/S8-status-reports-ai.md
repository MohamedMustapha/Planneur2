# S8 — Status Reports + AI Summaries

## Purpose
The **contextual status report** that auto-renders based on *who is consulting it* and their role (My work / My project team / My unit / My department / My project / Portfolio), plus **on-prem LLM** weekly/monthly narrative summaries. A team lead can auto-generate a status report (weekly, monthly) for their team; a dept-head for their department; and so on — always bounded by the visibility matrix.

## Depends on
S0–S7 (consumes Activities, Projects, Portfolio, Meetings; Scheduling optional).

## Module archetype
**DDD (orchestration).** No new persistent aggregate of note, but real application logic: scope resolution, projection assembly, AI prompt orchestration, caching, and export.

## What the report contains (by scope)
Scope defaults to the widest the viewer's role grants (see `visibility-matrix.md §6`), narrowable:
- **My work**: hours by activity type (build/run/qol/admin), 35h status, tasks pulled vs manual, my upcoming meetings/deadlines.
- **My project team**: team activity by department→function, iteration progress, blockers (from task states), upcoming copil.
- **My unit**: members' activity mix, RUN load (work-orders/shift coverage), QoL initiatives, kudos this month.
- **My department**: per-unit rollup, projects (with phase/portfolio state), cost snapshot (S3/S11), audits/patch-parties ahead.
- **My project** (lead/PO): iteration burn, cross-department contribution split, cost vs plan.
- **Portfolio** (PMO): counts by state (considered/committed/active/déphasé), déphasé this period, priority shifts.

## Domain / Application (`reporting` schema — mostly read + a small cache)
- `ReportRequest (scope_type, scope_id, period, language)` → resolves an authorized projection set (each underlying query carries its own RLS, so the report can never exceed the viewer's rights).
- `GeneratedSummary (id, scope_type, scope_id, period, language, model, prompt_hash, text, tokens, created_at)` — cached AI output; regenerated on demand or on schedule.
- Ports: `IActivityQueries`, `IProjectQueries`, `IPortfolioQueries`, `IMeetingQueries` (module contracts), `IAiSummarizer` (on-prem OpenAI-compatible client from S0).

## AI summarization
- The structured projection is turned into a compact, **PII-minimized** context and sent to the **on-prem** LLM with a role- and language-specific prompt ("Summarize this <scope> activity for <period> for a <role> audience, in <fr|es|en>; highlight risks, déphasé projects, QoL initiatives; be concise").
- Deterministic parts (numbers, tables) are computed in code, **not** by the LLM; the LLM only writes the narrative around them. Prompt + inputs hashed for cache; token/duration metrics to Prometheus.
- The LLM never receives data the viewer couldn't see (built from RLS-filtered projections only).

## API surface
- `GET /api/reports?scope=my|team|unit|department|project|portfolio&scopeId=&period=week|month|custom&lang=` — structured report (numbers + section keys).
- `POST /api/reports/summary` — generate/refresh the AI narrative for a report; returns cached if fresh.
- `GET /api/reports/{id}/export?format=pdf` — export (PDF via the pdf skill / server render), stored in RustFS, presigned link.
- Optional scheduled generation (weekly/monthly) per unit/department, materialized for the team board.

## RLS
No bypass: every projection query uses the same predicates as its owning module. Report scope selection is validated against `whoami`; requesting a scope beyond the viewer's role → `403`. The AI summary is generated only from authorized rows.

## Angular surface
- **Report view** that auto-selects scope from the viewer's role, with a scope narrower. Deterministic tables/charts render immediately (Signals + `httpResource`); the **AI narrative streams in** and is clearly labeled as machine-generated with a "regenerate" action.
- Language follows the user's UI language; export to PDF button.
- Team leads get a "generate weekly/monthly report" action; output can be pinned to the team board (S9 shows kudos there too).

## Integrations
Reads all upstream module contracts; calls the on-prem LLM (S0 `Ai`). Emits nothing critical.

## Tests
- **Unit:** scope resolution per role; deterministic aggregations (hours, counts, splits); prompt builder redacts PII and states language; cache keying by prompt hash.
- **Integration:** report endpoints incl. RLS (viewer cannot request a wider scope; numbers match seeded data); AI path against the **stub OpenAI server** (no real model in CI).
- **Architecture:** DDD layering; AI client behind a port.
- **E2E:** unit-head generates a weekly unit report (numbers correct, narrative present, French), exports PDF; a member requesting a department scope is denied.

## Acceptance criteria
- The report auto-adapts to the consulter's role/scope and never exceeds their visibility.
- Weekly/monthly summaries generate via the on-prem LLM in the user's language, with deterministic numbers computed in code.
- Export to PDF works and is stored in RustFS.

## Out of scope
Real model tuning. Emailing reports (dashboard/PDF only, per your note). BI warehouse.
