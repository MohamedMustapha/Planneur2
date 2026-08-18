# S1 — Directory (org backbone)

## Purpose
Model the organization and keep it in sync from LDAP (via Keycloak): **People**, **Departments**, **Units** (from LDAP `fonction`), and **functional roles**. This establishes the ambient `user → unit → department` context that RLS (S2) and every board depends on. It also holds **per-department configuration** — the mechanism that makes the platform department-agnostic.

## Depends on
S0.

## Module archetype
**2-layer CRUD + a sync service.** Local invariants only; the interesting logic is the LDAP reconciliation, not a domain lifecycle.

## Domain / Entities (`directory` schema)
- **Department** `(id, code, name_key, parent_department_id?, config_id)`.
- **Unit** `(id, department_id, code, name, ldap_fonction, kind)` — `kind ∈ {delivery, run, support, admin}` (hint for default boards).
- **Person** `(id, ldap_uid, display_name, email, primary_unit_id, timezone, ui_language, active)`.
- **PersonUnit** `(person_id, unit_id, is_primary)` — a person may belong to several units.
- **FunctionalRole** `(id, code, label_key)` — e.g. `dev`, `comptable`, `expert-comptable`, `architecte`, `tech-lead`, `chef-de-pole`. Seeded, extensible per department.
- **PersonFunctionalRole** `(person_id, functional_role_id, unit_id)`.
- **DepartmentConfig** `(id, department_id, activity_taxonomy_json, role_labels_json, kudo_rules_ref, default_board_layout, iteration_presets)` — the per-department knobs. Consumed by S5/S6/S9.

## Sync
- `LdapSyncService` (hosted, scheduled + on-demand endpoint). Source of truth = Keycloak's LDAP-federated users & groups. Maps:
  - LDAP user → Person (upsert by `ldap_uid`); deactivate missing.
  - LDAP `fonction` → Unit (+ its Department).
  - LDAP group memberships → FunctionalRole and (input to S2) contextual roles.
- Idempotent upserts by external key; runs under `system` context; diffs logged to SEQ; a Prometheus gauge exposes last-sync age & error count.

## API surface (all read-only to clients except admin config)
- `GET /api/directory/me` — the caller's person + units + roles (feeds the client signal store).
- `GET /api/directory/departments`, `/units`, `/people` (filtered by RLS).
- `GET /api/directory/departments/{id}/config`, `PUT …/config` (dept-head/PMO only).
- `POST /api/directory/sync` (admin/system) — trigger reconciliation, returns `202`.

## RLS
- **Person / Unit / Department:** readable by anyone authenticated *within visibility* — members read their unit + departments they belong to + people on their projects; heads read their department; PMO reads all. Directory is intentionally the most open module (you must see colleagues to collaborate), but still department-scoped for members.
- **DepartmentConfig:** read by anyone in the department; write only dept-head/PMO.
- Predicate `access.can_read_person(p_unit, p_dept)`; `can_read_department(p_dept)`.

## Angular surface
- **Org explorer**: department → unit → people tree (read).
- **Department settings** (dept-head/PMO): edit activity taxonomy, role labels (localized), iteration presets, default board layout, kudo rules link. Uses the per-department config; changes propagate via signal store to boards.
- `me` bootstraps the app context on load.

## Integrations
None external here (LDAP arrives via Keycloak). Config produced here is consumed by S5/S6/S9.

## Tests
- **Unit:** LDAP→entity mapping; config validation (taxonomy well-formed).
- **Integration:** sync idempotency (run twice → no dupes; removed user → deactivated); `me` returns correct units/roles; RLS matrix — member sees own unit/dept, not a foreign department's people.
- **Architecture:** module isolation.
- **E2E:** admin edits department taxonomy → a member in that department sees the new activity types offered (dependency proven later in S5, stubbed here).

## Acceptance criteria
- Running sync against the seeded LDAP produces the fixture org (2 departments, 2 units each) exactly.
- `me` drives the client context; language & timezone honored.
- Per-department config is editable and versioned (audit trail).

## Out of scope
Contextual-role *enforcement* (S2 wires roles into RLS). Activities (S5).
