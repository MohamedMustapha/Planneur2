# S5 — Activities

## Purpose
Let anyone **log their activity** against a per-department, configurable taxonomy — BUILD, RUN, quality-of-life, recruitment/admin, and whatever a department adds. Support **planned (opened) slots** and **actuals that differ**, within a **35h/week** legal guardrail. This is the data the individual, team, unit and department boards visualize (S6) and the reports summarize (S8).

## Depends on
S0–S3 (S4 optional link for iterations).

## Module archetype
**DDD** (`Modules/Activities`). Guardrails (35h), planned-vs-actual reconciliation, and taxonomy validation are genuine domain rules.

## Domain (`activities` schema)
**Aggregate: ActivityEntry**
- `ActivityEntry (id, person_id, unit_id, department_id, activity_type_id, project_id?, iteration_id?, kind, planned_start, planned_end, actual_start?, actual_end?, hours, source, external_ref?, note)`
  - `kind ∈ {planned, actual}` — a planned slot can be "opened" then filled with an actual; actuals may diverge from the plan (extensible).
  - `source ∈ {manual, azure-devops, servicenow}` (S10 sets the last two).
  - `external_ref` links to the pulled work item / ticket.
- **ActivityType** — **not fixed globally**; resolved from `DepartmentConfig.activity_taxonomy_json` (S1). Canonical top buckets every department inherits and can extend/relabel:
  - `project-build`, `project-run`, `quality-of-life` (maintenance / improvement / initiative), `recruitment-admin`. Departments add subtypes (e.g. HR: `payroll-run`, `hiring-campaign`).
- Value objects: `TimeSlot`, `WorkHours`.
- Domain events: `ActivityLogged`, `ActivityReconciled` (plan→actual).

**Invariants / guardrails**
- Per person per ISO week, **sum(actual hours) is checked against 35h**: soft-warn on exceed by default; a department can make it hard-block via config. Overtime flagged, not silently dropped.
- `project-build/run` entries require a `project_id` the person is a member of (RLS + validation).
- Actual slot must reference or supersede a planned slot when one exists in the window (reconciliation), but a bare actual (no prior plan) is allowed.

## API surface
- `POST /api/activities` — log (planned or actual).
- `PUT /api/activities/{id}`, `DELETE …` (own entries; leads within scope).
- `GET /api/activities?scope=me|unit|department|project&from=&to=` — RLS-filtered feed for boards/reports.
- `GET /api/activities/assignable-tasks?source=azure-devops|servicenow` — **the dropdown**: current-sprint or assigned-to-me tasks pulled read-only (S10), which a user picks to pre-fill an entry (project/type/ref auto-set).
- `GET /api/activities/weekly-summary/me?week=` — hours by type, 35h status.

## RLS
Policy = `access.can_read_activity(owner_id, unit_id, project_id, department_id)` from the matrix. Members read own + unit peers (kudos) + their projects; heads read dept + cross-dept shared; PMO all. Writes: own entries only for members; leads/heads within scope.

## Angular surface
- **Activity logging** for the individual (this is the primary daily interaction): the user sees **their team/unit context** and fills activity. Two input modes:
  1. **Timeline input** — create/drag a slot on their own Mobiscroll row (S6 renders it; S5 owns the write API). Actuals can differ from the planned slot.
  2. **Pull-from-source dropdown** — select a current-sprint or assigned task from Azure DevOps (devs) or ServiceNow (helpdesk); the entry pre-fills project + type + external ref, user sets hours.
- **Activity type picker** driven by the department taxonomy (localized labels).
- **35h meter** for the week with soft/hard behavior per department config.

## Integrations
- Consumes S10's `assignable-tasks` read models. Emits `Activities.Logged` for Reporting and board projections.

## Tests
- **Unit:** 35h guardrail (soft warn vs hard block per config); plan→actual reconciliation; project-membership requirement; taxonomy resolution from config.
- **Integration:** log endpoints incl. RLS matrix (member sees unit peers' activity but not a foreign unit's; head sees dept); assignable-tasks returns only tasks assigned to the caller.
- **Architecture:** DDD layering; no direct DbContext in Api.
- **E2E:** a dev logs a manual BUILD hour, then pulls a sprint task from the DevOps dropdown and logs against it; a helpdesk agent pulls a ServiceNow ticket; a lead views the unit feed.

## Acceptance criteria
- Any activity type (incl. department-custom) can be logged as planned or actual, with actuals able to differ.
- 35h/week enforced per department policy (soft/hard).
- Users can pull assigned/current-sprint tasks from Azure DevOps / ServiceNow into an entry.

## Out of scope
Rendering the boards (S6). Cross-source write-back (never — read-only). Report generation (S8).
