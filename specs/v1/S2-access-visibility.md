# S2 — Access & Visibility

## Purpose
Turn the directory into an enforced **authorization model**: resolve contextual roles (from LDAP groups with an in-app **RBAC fallback view**), and materialize the **RLS policies** and predicate functions that every data slice will attach to. This slice makes `visibility-matrix.md` executable.

## Depends on
S0, S1.

## Module archetype
Infrastructure (`Modules/Access`) — no user-facing CRUD beyond the RBAC admin view.

## Domain / Entities (`access` schema)
- **ContextualRoleAssignment** `(id, person_id, role, scope_type, scope_id, source)` — `role ∈ {member, unit-head, dept-head, project-lead, po, pmo}`, `scope_type ∈ {unit, department, project, global}`, `source ∈ {ldap, rbac-override}`.
- **RbacOverride** `(id, person_id, role, scope_type, scope_id, reason, expires_at?)` — the fallback: grant/deny when LDAP is incomplete. Audited.
- Predicate functions (from the matrix): `uid/unit/depts/roles/has/is_head`, `on_project`, `project_in_my_depts`, `can_read_activity`, `can_read_project`, `can_read_kudo`, `can_read_meeting`, `can_read_schedule_row`. Created here; **used** by later slices' migrations.

## Role resolution
- Primary: LDAP group → contextual role (mapper config in Keycloak, materialized into `ContextualRoleAssignment` during S1 sync).
- Fallback: `RbacOverride` rows merged on top at resolution time. Effective roles = LDAP roles ∪ active overrides.
- The resolved roles are what the BFF puts in the token / `IUserContext`, and what the interceptor writes into `app.roles`.

## API surface
- `GET /api/access/roles/{personId}` — effective roles (head/PMO only).
- `GET/POST/DELETE /api/access/overrides` — manage RBAC overrides (dept-head within dept, PMO globally). All writes audited.
- `GET /api/access/whoami` — the caller's effective roles & scopes (client can gate nav).

## RLS
This slice **defines** the shared predicates and installs policies on the Access tables themselves:
- `ContextualRoleAssignment` / `RbacOverride`: readable by heads within scope and PMO; writable per §5 of the matrix. Members may read only their own assignments.

## Angular surface
- **RBAC admin** (dept-head/PMO): search a person, view effective roles, add/remove an override with reason & expiry. Clear "source: LDAP vs override" badges.
- Nav/route guards consume `whoami`; but every data call still relies on RLS server-side (guards are UX only).

## Tests
- **Unit:** effective-role merge (LDAP ∪ overrides, expiry respected, deny beats grant if modeled).
- **Integration (the keystone):** the **RLS matrix test fixture** — seed the canonical org (2 depts × 2 units, people per unit, one cross-department project). For each contextual role, connect with that context and assert the exact visible set for a probe table. This fixture is reused by S3/S5/S6/S8/S9.
- **Architecture:** later slices must reference `access.*` predicates, not hand-rolled role checks (custom rule).
- **E2E:** log in as member vs dept-head; confirm the dept-head sees a second unit's people and the member does not.

## Acceptance criteria
- Effective roles resolve correctly from LDAP and can be overridden via RBAC with audit.
- The shared predicate functions exist and are unit-tested at the SQL level.
- The RLS matrix fixture passes and is exported for reuse.

## Out of scope
Business entities themselves — this slice only provides the access primitives they attach to.
