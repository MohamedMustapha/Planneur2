# v2 · 06 — Strategy → Objectives → Projects (new · S13)

**New slice.** A spine that connects intent to execution: a **Strategy** (at service or node level) sets **Objectives**; **portfolio items** (`03`) are linked as the work that moves each objective; progress rolls up. COPIL meetings (`07`) review it.

## 1. Model (`strategy` schema)

- **Strategy** `(id, scope_type, scope_id, period_from, period_to, title, narrative, owner_person_id, status)`
  - `scope_type ∈ {service, node}`; `status ∈ {draft, active, closed}`.
- **Objective** `(id, strategy_id, title, description, metric_kind, baseline, target, current, unit, due, status, weight)`
  - `metric_kind ∈ {number, percent, currency, milestone, qualitative}`.
  - `status ∈ {on-track, at-risk, off-track, done}` — derived from current vs target vs due, overridable.
- **ObjectiveContribution** `(objective_id, item_id, weight, note)` — which portfolio items contribute, and how much. Also accepts a **Problem** (`05`) as a contribution source ("solving this pain advances the objective").
- **KeyResult** (optional, if OKR-style) `(id, objective_id, title, target, current)` — sub-measures.

## 2. Rollup
- Objective progress = current/target (bounded), or milestone/qualitative status.
- Strategy progress = weighted objective progress. Each contributing item shows, on its identity card (`03`), the objectives it serves; each objective shows its contributing items and their state (iteration/epic progress from `03`, budget health from `04`).
- **Alignment gaps** surfaced: objectives with no contributing item; items with no objective (candidate to deprioritize).

## 3. API surface
- `GET/POST/PATCH /api/strategy` (scope service/node — heads/PMO).
- `GET/POST/PATCH/DELETE /api/strategy/{id}/objectives` (+ key-results).
- `POST /api/strategy/objectives/{oid}/contributions` (link item/problem, weight), `DELETE …`.
- `GET /api/strategy/{id}/rollup` — objectives with progress + contributing items' states.
- `GET /api/strategy/alignment` — gaps (unlinked objectives / unlinked items).

## 4. RLS
`access.can_read_objective(scope)`: service/node strategy readable by that scope's members (read) and its head (edit); PMO all; cross-scope read for heads (knowledge flow). Members see objectives their items contribute to. Edit: node-head (node strategy), node-head (service strategy), PMO.

## 5. Angular surface
- **Strategy overview** (service or node): objectives as cards with progress rings, status, due; contributing items listed with mini-state.
- **Objective detail**: metric + trend, contributing items/problems, key results, history.
- **Alignment view**: unlinked objectives / unlinked items — one click to link or to open the `03` create wizard from an objective.
- Read-only summary block embeddable in COPIL CRs (`07`) and service/node reports (S8/`07`).

## Tests
- **Unit:** progress rollup (weighted; metric kinds; status derivation); alignment gap detection.
- **Integration:** RLS (member reads objectives their item serves; only heads edit); contribution links reflect on the item card.
- **Architecture:** DDD layering.
- **E2E:** a node-head defines an objective "cut ticket handling time 20%", links the converted problem's project (`05`→`03`); as the project's epics complete, the objective progress moves; the COPIL CR embeds the rollup.
