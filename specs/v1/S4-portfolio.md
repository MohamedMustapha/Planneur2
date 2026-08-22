# S4 — Portfolio

## Purpose
Govern the set of projects over time: a **lifecycle state machine** (considered → committed → active → déphasé/archived), **free-length iterations** with quick selectors (1w / 2w / 1m / custom), and a **portfolio board** that shows which projects/iterations are running, candidate, or retired.

## Depends on
S0–S3.

## Module archetype
**DDD** (`Modules/Portfolio`). A state machine with guarded transitions and audit is the definition of a domain module.

## Domain (`portfolio` schema)
**Aggregate: PortfolioItem** (1:1 with a Project, or a lightweight stub for `considered` projects not yet in S3)
- `PortfolioItem (id, project_id?, name_key, state, priority, considered_at, committed_at?, activated_at?, archived_at?, decision_notes)`
  - `state ∈ {considered, committed, active, dephase}` (`dephase` = archived / removed from prod).
  - `considered` items may exist **without a full Project** (candidate not yet committed); on commit, a Project (S3) is created/linked.
- `Iteration (id, portfolio_item_id, sequence, name, length_preset, starts_on, ends_on, state)`
  - `length_preset ∈ {1w, 2w, 1m, custom}` — a selector that pre-fills `ends_on`; free-form override allowed.
  - `state ∈ {planned, active, done, cancelled}`.
- Domain events: `ItemConsidered/Committed/Activated/Archived`, `IterationOpened/Closed`.

**Transition guards**
- `considered → committed`: requires a decision (owner + notes); creates/links a Project.
- `committed → active`: requires ≥1 planned iteration and a team.
- `active → dephase`: archives; open iterations auto-cancelled; downstream scheduling frozen (read-only).
- Backwards transitions only by PMO, audited.

## API surface
- `GET /api/portfolio` — the board data, RLS-filtered, grouped by state.
- `POST /api/portfolio/considered` — register a candidate (name, priority, notes).
- `POST /api/portfolio/{id}/commit | /activate | /archive` — guarded transitions.
- `POST /api/portfolio/{id}/iterations` (preset selector), `PUT/DELETE …/iterations/{iid}`, `POST …/iterations/{iid}/close`.
- `GET /api/portfolio/{id}/iterations` — timeline of iterations.

## RLS
`access.can_read_project` reused via the linked project; `considered` stubs (no project yet) are visible to their author, the owning department's head, and PMO. Transitions restricted per matrix §5.

## Angular surface
- **Portfolio board**: columns/lanes by state (Considered · Committed · Active · Déphasé). Cards show priority, lead department, cost (from S3), current iteration. Drag/quick-action to transition (guards enforced server-side; UI reflects allowed actions from `whoami`).
- **Iteration strip** per active item: the 1w/2w/1m/custom selector pre-fills dates; iterations shown as a compact timeline (Mobiscroll optional here; the heavy timeline is S6).
- Filters: my projects / my unit / my department / all (bounded by role).

## Integrations
Consumes `Projects.*`; emits `Portfolio.ItemActivated/Archived` so Scheduling can open/freeze boards and Reporting can flag phase status. When an item is archived, Scheduling (S6) marks its rows read-only.

## Tests
- **Unit:** every transition guard; iteration preset date math; archive cascades (cancel open iterations).
- **Integration:** transition endpoints incl. RLS (project-lead can activate own; PMO can force-archive; member cannot); considered-stub visibility.
- **Architecture:** DDD layering.
- **E2E:** register a considered candidate → commit (project created) → activate with a 2-week iteration → archive; assert board reflects each move and archived board is read-only.

## Acceptance criteria
- Full lifecycle with guarded, audited transitions; `dephase` = archived/removed-from-prod.
- Free iterations with 1w/2w/1m/custom quick selectors.
- Board is role-scoped and drives downstream open/freeze signals.

## Out of scope
The rich RUN/BUILD timelines (S6). Cost derivation (S11).
