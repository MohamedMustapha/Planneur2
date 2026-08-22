# S11 — Capex/Opex View (Finance)

## Purpose
A **capitalization view** for department/IT heads: split project cost and effort into **Capex** (capitalizable BUILD investment) vs **Opex** (RUN / maintenance), using the BUILD/RUN classification (S3), logged effort (S5) and an optional rate card. A "nice addition," explicitly head-scoped.

## Depends on
S0–S5.

## Module archetype
**2-layer query.** Mostly derivation over existing data + a small rate-card table. No lifecycle.

## Domain / Entities (`finance` schema)
- **RateCard** `(id, department_id, functional_role_id, hourly_rate, currency, effective_from, effective_to?)` — optional; if absent, effort is shown in hours only.
- **CapexOpexRule** `(id, department_id, build_treatment, run_treatment, qol_treatment, admin_treatment)` — maps each activity bucket to `capex|opex|excluded`. Default: BUILD→capex, RUN→opex, QoL→opex, admin→excluded. Editable per department.
- **Derived views** (SQL/materialized): per project & per period —
  - `manual_cost` (from S3), `effort_hours` by bucket (from S5), `effort_cost` (hours × rate card), and the **capex/opex split** by applying `CapexOpexRule`.

## API surface
- `GET /api/finance/capex-opex?scope=department|project|portfolio&scopeId=&period=` — the split (amounts + hours), RLS-filtered to heads/PMO.
- `GET/PUT /api/finance/rate-cards`, `GET/PUT /api/finance/rules` (dept-head/PMO).
- `GET /api/finance/capex-opex/export?format=xlsx` — export via the xlsx skill, stored in RustFS.

## RLS
Strictly heads/PMO: `access.is_head()` (dept-scoped) or `access.has('pmo')`. Members and project-leads without a head role get `403`. Rate cards & rules editable by dept-head (own dept) / PMO.

## Angular surface
- **Capex/Opex dashboard**: per department, a Capex vs Opex breakdown (by project, by period), effort-derived and manual-cost columns side by side; toggles for "include rate-card effort cost." Charts + a table; export to Excel.
- Rule/rate-card editors (heads only).

## Integrations
Consumes S3 (cost, classification), S5 (effort by bucket). Emits nothing.

## Tests
- **Unit:** rule application (bucket → capex/opex/excluded); rate-card effective-dating; split math.
- **Integration:** endpoints incl. RLS (member/project-lead denied; dept-head sees own dept only; PMO all); numbers reconcile to seeded activities & costs.
- **Architecture:** 2-layer isolation.
- **E2E:** a dept-head sets a rate card, views the capex/opex split for the department, exports to xlsx; a member is denied.

## Acceptance criteria
- BUILD/RUN (+QoL/admin) effort and manual cost split into Capex/Opex per configurable rules, optionally valued via rate cards.
- View is head/PMO-only and exportable to Excel.

## Out of scope
Accounting-system integration, GL postings, amortization schedules. This is a management view, not the books.
