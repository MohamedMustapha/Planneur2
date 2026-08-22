# v2 · 09 — Migration Runbook (v1 → v2)

**Purpose:** move the running v1 database and codebase to the 4-level org and the `PortfolioItem` model **without a big-bang rewrite** and without a window where RLS is off. Every phase is independently deployable and reversible.

> **Non-negotiable:** RLS is never disabled to make a migration easier. Migrations run as `app_owner`; the app keeps running as `app_rw`. If a step seems to require dropping a policy, replace the policy in the same transaction instead.

---

## Phase 0 — Safety net (before anything)

1. Full logical dump (`pg_dump -Fc`) + verified restore into a scratch database. **Rehearse the whole runbook there first.**
2. Snapshot the RLS matrix fixture output (S2) as a **golden file**: the exact visible-row sets per role today. Every later phase re-runs it and diffs.
3. Freeze schema changes on `main` for the duration, or run the migration on a branch environment.
4. Record row counts per table; each phase asserts no unintended loss.

---

## Phase 1 — Introduce the node tree (additive, zero behavior change)

```sql
create table directory.org_level (
  level_no int primary key check (level_no between 1 and 8),
  code text unique not null, label_key text not null, label_plural_key text not null,
  head_label_key text not null,
  people_allowed boolean not null default true, is_optional boolean not null default false);

create table directory.org_node (
  id uuid primary key, parent_id uuid references directory.org_node(id),
  level_no int not null references directory.org_level(level_no),
  code text not null, name text not null,
  ancestor_ids uuid[] not null, head_person_id uuid, profile_id uuid,
  active boolean not null default true, unique (parent_id, code));
create index on directory.org_node using gin (ancestor_ids);
```
Seed `org_level` with the deployment's four labels. Trigger maintains `ancestor_ids` on insert and on re-parent (recomputes the whole subtree).

**Checkpoint:** app untouched, golden RLS diff empty. Deploy.

## Phase 2 — Project the existing org into the tree (dual-read)

```sql
-- one placeholder top node
insert into directory.org_node (id, parent_id, level_no, code, name, ancestor_ids)
  values (:root, null, 1, 'A-RECLASSER', 'À reclasser', array[:root]);
-- departments → level 2, units → level 3 (ancestor_ids filled by trigger)
insert into directory.org_node (id, parent_id, level_no, code, name, head_person_id)
  select d.id, :root, 2, d.code, d.name, d.head_person_id from directory.department d;
insert into directory.org_node (id, parent_id, level_no, code, name, head_person_id)
  select u.id, u.department_id, 3, u.code, u.name, u.head_person_id from directory.unit u;
```
Ids are **reused**, so every existing FK keeps pointing at a valid node. Old tables stay in place, read-only, for one release.

**Checkpoint:** `org_node` row count == departments + units + 1. Golden diff empty. Deploy.

## Phase 3 — Attach people and scoped rows to nodes

```sql
alter table directory.person
  add column home_node_id uuid references directory.org_node(id),
  add column node_ancestor_ids uuid[];
update directory.person set home_node_id = coalesce(primary_unit_id, department_id);
update directory.person p set node_ancestor_ids = n.ancestor_ids
  from directory.org_node n where n.id = p.home_node_id;
alter table directory.person alter column home_node_id set not null,
                             alter column node_ancestor_ids set not null;
```
Then, **per module**, add `node_id` + `node_ancestor_ids`, backfill from the owning person/department, set NOT NULL, and GIN-index:
```sql
alter table activities.activity_entry add column node_id uuid, add column node_ancestor_ids uuid[];
update activities.activity_entry a set node_id = coalesce(a.unit_id, a.department_id);
update activities.activity_entry a set node_ancestor_ids = n.ancestor_ids
  from directory.org_node n where n.id = a.node_id;
alter table activities.activity_entry alter column node_id set not null,
                                      alter column node_ancestor_ids set not null;
create index on activities.activity_entry using gin (node_ancestor_ids);
```
**Checkpoint:** for every scoped table, `node_id` non-null and consistent with the legacy columns (assert equality). Golden diff still empty — no predicate has changed yet. Deploy.

## Phase 4 — Swap roles and predicates (the behavior change)

1. Collapse roles: every `dept-head`/`unit-head` assignment becomes **`node-head` on the corresponding node id** (ids were reused, so this is a straight insert). Add `service-head`-equivalents by assigning `node-head` on level-1 nodes once Phase 6 re-parenting is done.
2. Install the `01 §3` predicate set.
3. `alter policy` per module, **one transaction each**, to call the new predicates with `(node_id, node_ancestor_ids)`.
4. Emit new GUCs from the request interceptor (`app.node_id`, `app.node_path`, `app.headed_nodes`); stop emitting the old ones.

**Checkpoint — the critical gate:** the golden diff must now show **exactly** heads gaining their full subtree (an old unit-head sees the same rows; an old dept-head now also sees people attached directly at level 2) and nothing else. Any other row is stop-the-line.

Then drop the legacy columns and the `department`/`unit` tables after one shim release.

## Phase 5 — New capability modules

Additive, no migration risk, order per README: `04` Finance (replaces S11 tables — keep old capex/opex views until the new roll-up is verified against them), `05` Problems, `06` Strategy, `07` Minutes, `08` Admin. Each ships with its own RLS policies from day one.

---

## Phase 6 — Re-parent & archetype assignment (data curation, in-app)

Not a migration script — **admin work in the UI** (`08`):
1. Global admin creates the real top-level nodes and re-parents each level-2 node out of `A-RECLASSER` (the trigger recomputes `ancestor_ids` for whole subtrees).
2. Attaches a **node profile** (`10`) at the right depth → taxonomy, boards, capabilities, budget rules inherit downward.
3. Confirms people attached directly at intermediate levels, and node heads at each depth.
4. Deletes the placeholder service once empty (guarded: refuse while it has children).

**Definition of done for the whole migration:** `A-RECLASSER` no longer exists, and no code references a level by name.

---

## Phase 7 — UI re-shell

Ship `02` (role nav + Focus mode + theme) **last**, as one release. Doing it earlier means re-doing it as entities change; doing it last means users see one coherent change instead of six confusing ones.

---

## Rollback strategy

| Phase | Rollback |
|---|---|
| 1 | Drop `org_node`/`org_level`. Trivially reversible. |
| 2 | Delete the projected nodes; legacy tables were never touched. |
| 3 | Drop the added columns. Legacy columns still authoritative. |
| 4 | Restore prior predicate bodies (`create or replace`) + re-`alter policy` + re-emit old GUCs. **Take a dump immediately before this phase** — it is the first irreversible-in-practice step. |
| 5–7 | Feature-flag off; additive only. |

## Tests specific to migration
- **Golden-diff test** after every phase (the single most important gate).
- **Row-count invariance** per table per phase.
- **Idempotency:** each script runnable twice with no effect the second time.
- **Rehearsal gate:** CI restores yesterday's production dump into a container and runs Phases 1–4 end to end nightly until go-live.
