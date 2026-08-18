# S6 — Timeline Views (Mobiscroll)

## Purpose
The visual heart of the product: the **rotating Mobiscroll timeline** used for three RUN/BUILD archetypes and composed into the **My / Team / Unit / Department / Project** boards. This slice mostly **renders and assigns** over data owned by Activities (S5), Projects (S3), Portfolio (S4), Meetings (S7).

## Depends on
S0–S5 (S7 for special days/meetings overlays; can ship boards first, overlay S7 later).

## Module archetype
**Mixed.** Read-heavy **query/projection** side (2-layer read models) + a small **assignment** command side (assign/unassign work orders, plan shifts) that has enough rules to warrant a thin domain. Board *definitions* come from `DepartmentConfig.default_board_layout` (S1).

## The three Mobiscroll archetypes (map to your demo links)

### 6a — RUN · Work-Order Assignment
Model: <https://demo.mobiscroll.com/jquery/timeline/assign-unassign-work-orders-fixed-top-row>
- **Rows = people** in the RUN unit; **fixed top row = unassigned work orders** pool. Drag a work order from the pool onto a person's row to assign; drag back to unassign.
- Work orders are RUN tasks: manual, or **pulled read-only from ServiceNow** (S10) as unassigned items. Assigning creates/links an `ActivityEntry` (kind=planned, type=project-run) for that person.
- Used by e.g. Helpdesk: tickets flow into the top row, the lead distributes them across the team.

### 6b — RUN · Shift Scheduler
Model: <https://demo.mobiscroll.com/jquery/timeline/employee-shifts>
- **Rows = people**, columns = time; cells = shifts (morning/afternoon/on-call). Shift templates per department config. Coverage checks (min staffing per slot) surfaced as warnings.
- Produces planned `ActivityEntry` slots of the shift type; conflicts (double-booking, >35h) flagged.

### 6c — BUILD · Task Progress
Model: <https://demo.mobiscroll.com/jquery/timeline/show-task-progress-on-event>
- **Rows = project members** (grouped by department → function, per S3 team projection); events = tasks/iterations with a **progress bar** on the event. Progress reflects actuals vs plan (S5) and/or the linked Azure DevOps item's state (S10, read-only).
- Iteration boundaries (S4) shown as ranges; drag to reschedule updates planned slots.

## Composed boards (who sees what — same rows, different scope)

| Board | Rows | Cells | Primary archetype | Default viewer |
|---|---|---|---|---|
| **My board** | my activity lanes (build/run/qol/admin) | my planned+actual slots | 6c-style progress + free slots | member |
| **Team board** | my unit's members | their activity | 6a for RUN units, 6c for BUILD units | unit-head, member (read) |
| **Unit board** | the unit's members across their projects | activities per project line | mixed | unit-head |
| **Project board** | project members **grouped by department** | tasks/progress | 6c | project-lead / PO |
| **Department board** | each **unit** of the department, each project on a line | activities; **special days & deadlines overlaid** | 6a/6c mix | dept-head |

- The **project view**: each line = project members divided by their department (dev, designer, ops…) — driven by S3's grouped team projection.
- The **department view**: every department **unit** with each project on a line, the board filled with activities; **deadlines / special days** (patch party, audit — from S7) rendered as marked columns/badges.

## API surface (read models + assignment commands)
- `GET /api/scheduling/board?type=my|team|unit|project|department&scopeId=&from=&to=` — the composed timeline payload (resources/rows + events + overlays), RLS-filtered, shaped for Mobiscroll.
- `POST /api/scheduling/work-orders/{id}/assign` / `/unassign` (6a) — moves the pool item to a person, creates/removes the planned activity.
- `POST /api/scheduling/shifts` / `PUT` / `DELETE` (6b) — shift CRUD with coverage & 35h checks.
- `PUT /api/scheduling/tasks/{id}/schedule` (6c) — reschedule/rescale a task event (updates planned slots).
- `GET /api/scheduling/work-orders/pool?source=servicenow` — the unassigned top-row pool (read-only pull).

## RLS
Board rows and events are projections over Activities/Projects/Meetings; each underlying query already carries its own RLS predicate, so the board is a **join of already-authorized sets** — a member's team board shows unit peers (allowed), a dept-head's department board shows all units (allowed), etc. Assignment commands re-check scope (`can_read_project`/unit membership) before writing.

## Angular surface
- **Mobiscroll Angular Timeline** component wrapped once, configured per board via a `BoardConfig` signal (resources, event templates, drag rules, overlays). The "rotating" timeline (horizontal scroll across days/weeks, resource rows) is the shared canvas; each board supplies data + templates.
- Drag/drop for 6a assignment and 6c reschedule; shift editor for 6b. Progress bars on 6c events.
- Overlays: special days & deadlines (S7) as marked columns; meeting bands.
- Board switcher respects role (from `whoami`); scope selector (project/unit/department) bounded by RLS.
- Fully i18n'd (fr/en/es) incl. Mobiscroll locale.

## Integrations
Reads S10 pools (ServiceNow work orders, Azure DevOps tasks) read-only. Writes only local planned activities.

## Tests
- **Unit:** board payload shaping; coverage & 35h checks for shifts; assignment→activity creation.
- **Integration:** each board type incl. RLS (member team board = unit peers only; dept board = all units; project board = cross-dept members); assign/unassign round-trips create/remove the right planned activity.
- **Architecture:** query side stays 2-layer; assignment side isolated.
- **E2E (Playwright, per archetype):**
  - 6a: lead drags a ServiceNow work order from the pool onto an agent → activity appears on that agent's row.
  - 6b: build a week's shifts, trigger a coverage warning, resolve it.
  - 6c: reschedule a task; progress bar reflects logged actuals.
  - Department board: a patch-party special day and an audit deadline show as marked columns.

## Acceptance criteria
- All three archetypes render on the shared rotating timeline and are wired to the correct data.
- The five composed boards render the right rows/scope per role, with the project view grouped by department and the department view overlaying special days/deadlines.
- Assignments/shifts/reschedules write valid planned activities within RLS.

## Out of scope
Owning activity rules (S5). Meetings/special-day CRUD (S7). Report text (S8).
