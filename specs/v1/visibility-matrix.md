# Visibility Matrix (RLS + report scoping)

This is the **single source of truth** for who can see what. It is enforced primarily by Postgres RLS and mirrored by the contextual status report (S8). Every data slice implements the RLS matrix test against this document.

## 1. Contextual roles

Resolved from LDAP groups, with an in-app **RBAC fallback view** (Access module) that can grant/override when LDAP is incomplete.

| Role | Scope of read | Can act |
|---|---|---|
| **member** | Own activity; the team of every project they're on; **their whole unit** (needed to give kudos to teammates). | Log own activity; give kudos to unit peers; assign self from integration task lists. |
| **unit-head** (chef de pôle for a unit) | Everything in their **unit**; projects their unit contributes to. | Everything a member can + assign/plan work in the unit; approve unit-scoped items. |
| **dept-head** | Everything in their **department**, **and cross-department read** on shared projects for knowledge flow. | Plan across the department; configure the department (taxonomy, roles, kudo rules). |
| **project-lead / PO** | The **whole project** they lead, across all contributing departments. | Manage project team, iterations, portfolio state transitions for their project. |
| **PMO** | **Everything** portfolio-wide (read); portfolio governance. | Portfolio-level transitions, cross-project reporting. |
| **system** | Read-all for background jobs (sync, AI, outbox) where policies permit. | Writes via sync/outbox only. |

**Cross-department knowledge flow (decided):** heads (unit-head, dept-head, PMO) get **read** visibility across department boundaries on shared projects. Members do **not** — a member sees other departments' contributions only within projects they are personally on. This is the one deliberate widening beyond strict department isolation.

## 2. Session GUCs (set per request — see architecture §4)

| GUC | Content |
|---|---|
| `app.user_id` | current person id |
| `app.unit_id` | current person's unit id |
| `app.dept_ids` | csv of department ids the person belongs to (usually one) |
| `app.roles` | csv of contextual roles (e.g. `member,unit-head`) |

Helper accessors (in `access` schema):
```sql
create function access.uid()   returns uuid       language sql stable as $$ select nullif(current_setting('app.user_id', true),'')::uuid $$;
create function access.unit()  returns uuid       language sql stable as $$ select nullif(current_setting('app.unit_id', true),'')::uuid $$;
create function access.depts() returns uuid[]     language sql stable as $$ select string_to_array(nullif(current_setting('app.dept_ids', true),''), ',')::uuid[] $$;
create function access.roles() returns text[]     language sql stable as $$ select string_to_array(coalesce(current_setting('app.roles', true),''), ',') $$;
create function access.has(role text) returns boolean language sql stable as $$ select role = any(access.roles()) $$;
create function access.is_head() returns boolean language sql stable as $$ select access.has('unit-head') or access.has('dept-head') or access.has('pmo') $$;
```

## 3. Central predicate functions

Each returns whether the current session may **read** the row. Slices reference these in their policies.

```sql
-- membership helper: is the current user on this project's team?
create function access.on_project(p_project uuid) returns boolean language sql stable as $$
  select exists (select 1 from projects.project_member m
                 where m.project_id = p_project and m.person_id = access.uid());
$$;

-- is this project shared with one of my departments?
create function access.project_in_my_depts(p_project uuid) returns boolean language sql stable as $$
  select exists (select 1 from projects.project_department pd
                 where pd.project_id = p_project and pd.department_id = any(access.depts()));
$$;

-- ACTIVITY visibility
create function access.can_read_activity(p_owner uuid, p_unit uuid, p_project uuid, p_dept uuid)
returns boolean language sql stable as $$
  select
      p_owner = access.uid()                                   -- own
   or (access.has('member')     and p_unit = access.unit())    -- my unit (peers, for kudos)
   or (access.has('unit-head')  and p_unit = access.unit())    -- unit head: whole unit
   or (access.has('dept-head')  and p_dept = any(access.depts())) -- dept head: whole dept
   or (access.has('dept-head')  and access.project_in_my_depts(p_project)) -- + shared projects (knowledge flow)
   or ((access.has('project-lead') or access.has('po')) and access.on_project(p_project)) -- project scope
   or access.has('pmo')                                        -- PMO: all
   or access.has('system');
$$;
```

Analogous functions exist per scoped entity: `can_read_project`, `can_read_kudo`, `can_read_meeting`, `can_read_schedule_row`. They follow the same shape; the slice files state the exact predicate.

## 4. Read matrix (summary)

| Entity ↓ / Role → | member | unit-head | dept-head | project-lead/PO | PMO |
|---|---|---|---|---|---|
| Own activity | ✅ | ✅ | ✅ | ✅ | ✅ |
| Unit peers' activity | ✅ (own unit) | ✅ (own unit) | ✅ (dept) | ➖ (only their project) | ✅ |
| Other units in dept | ➖ | ➖ | ✅ | ➖ | ✅ |
| Cross-dept on shared project | ➖ | project only | ✅ (read, knowledge flow) | ✅ (their project) | ✅ |
| Project team & progress | ✅ (projects they're on) | ✅ (unit's projects) | ✅ (dept + shared) | ✅ (their project) | ✅ |
| Portfolio board | own+unit projects | unit projects | dept projects | their project | all |
| Kudos | give/see within unit | unit | dept | project | all |
| Meetings/special days | ones targeting them/unit | unit | dept | project | all |
| Capex/Opex (Finance) | ➖ | ➖ | ✅ (dept) | ➖ | ✅ |

✅ = full read · ➖ = not visible.

## 5. Write rules (enforced in application + policy where feasible)

- Members write **only their own** activity and kudos they author.
- Heads write within their scope; project-leads within their project.
- Portfolio state transitions: project-lead/PO for their project, PMO for governance transitions (e.g. force-archive). All transitions audited.
- Integrations and AI write under `system` only.

## 6. Report scoping (S8 must match this exactly)

The contextual status report renders **the widest scope the viewer's role grants**, then lets them narrow:

| Viewer | Default report scope | Narrow-to options |
|---|---|---|
| dev / ops / member | *My work* + *my project team(s)* | per project |
| unit-head | *My unit* | per member, per project |
| PO / project-lead | *My project* | per iteration, per department contribution |
| dept-head | *My department* | per unit, per project, cross-dept shared |
| PMO | *Portfolio* | per department, per project, per state |

The report never shows data the RLS layer would deny; it is a projection over already-authorized rows, so the two can't diverge.
