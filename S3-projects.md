# S3 — Projects

## Purpose
The core delivery entity: a **Project** with a manual cost, one or more contributing **departments**, a **cross-department team**, and a **BUILD/RUN classification**. Projects are the spine that Activities (S5), Scheduling (S6), Portfolio (S4), Reporting (S8) and Finance (S11) hang off.

## Depends on
S0, S1, S2.

## Module archetype
**DDD** (`Modules/Projects`). It has real invariants (a team member must belong to a contributing department; cost/classification rules; team composition across departments) and emits integration events other modules react to.

## Domain (`projects` schema)
**Aggregate: Project**
- `Project (id, code, name_key, description, classification, cost_amount, cost_currency, cost_notes, status_ref, owner_person_id, created/modified audit)`
  - `classification ∈ {build, run, mixed}`.
  - `cost_*` entered manually (S11 adds the capex/opex derivation on top; not here).
- `ProjectDepartment (project_id, department_id, is_lead_department)` — the departments contributing. At least one; exactly one lead.
- `ProjectMember (project_id, person_id, department_id, functional_role_id, allocation_pct?, from, to?)` — the team, **explicitly tagged by department and function**, which is what lets the project view render "members divided by department (dev / designer / ops…)".
- Value objects: `Money`, `Allocation`, `Classification`.
- Domain events: `ProjectCreated`, `ProjectMemberAdded/Removed`, `ProjectClassificationChanged`, `ProjectCostChanged` → integration events `Projects.*` on the outbox.

**Invariants**
- A `ProjectMember.person` must belong to a `ProjectDepartment` of the project (validated against Directory via a query port).
- Exactly one lead department; lead cannot be removed while others depend on it.
- Cost non-negative; currency from an allowed set.

## API surface (`Api`, thin → mediator)
- `POST /api/projects` (project-lead/PO/dept-head/PMO) — create with classification, cost, departments.
- `GET /api/projects`, `GET /api/projects/{id}` — RLS-filtered.
- `PUT /api/projects/{id}` — name/desc/cost/classification (audited).
- `POST /api/projects/{id}/members`, `DELETE …/members/{personId}`.
- `POST /api/projects/{id}/departments`, `DELETE …/departments/{deptId}`.
- `GET /api/projects/{id}/team` — team grouped **by department then functional role** (the project-view projection).

## RLS
Policy on Project & children uses `access.can_read_project(p_project)`:
```sql
create function access.can_read_project(p_project uuid) returns boolean language sql stable as $$
  select access.has('pmo')
      or access.on_project(p_project)                                 -- member on it
      or (access.is_head() and access.project_in_my_depts(p_project)) -- head: any project touching my dept
      or ((access.has('project-lead') or access.has('po'))
           and exists (select 1 from projects.project p               -- lead of it
                       where p.id = p_project and p.owner_person_id = access.uid()));
$$;
```
Cross-department knowledge flow lands here: a **dept-head sees any project that touches their department**, including the other departments' members on it. Writes: project-lead/PO of the project or a head of a contributing department; PMO always.

## Angular surface
- **Project list** (RLS-filtered) with classification & status chips.
- **Project detail**: header (cost, classification, lead dept), and the **team panel grouped by department → functional role** — this is the "each line shows project members divided by their department" view. Add/remove member with department+function pickers sourced from Directory.
- Cost editor (manual) with currency + notes.

## Integrations
Emits `Projects.ProjectCreated/Updated/MemberChanged` for Portfolio, Scheduling, Reporting, Finance. Consumes Directory contracts for person/department validation.

## Tests
- **Unit:** invariants (member must be in a contributing dept; one lead dept; cost rules); classification change events.
- **Integration:** endpoint→handler→DB incl. RLS matrix — dept-head sees a cross-dept project's foreign members; a member on the project sees the team; an unrelated member sees nothing.
- **Architecture:** DDD layering (Domain refs nothing; Api → Application only).
- **E2E:** create a cross-department project, add a dev (dept A) and a designer (dept B), verify the team panel groups them by department for a dept-head viewer.

## Acceptance criteria
- Projects carry manual cost, classification, ≥1 department with one lead, and a department-tagged team.
- The team projection groups by department→function for the project view.
- RLS gives heads cross-department read on shared projects; members are project/unit-scoped.

## Out of scope
Lifecycle states & iterations (S4). Activities/scheduling (S5/S6). Capex/opex (S11).
