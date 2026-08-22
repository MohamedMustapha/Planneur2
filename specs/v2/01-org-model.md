# v2 · 01 — Org Model: generic N-level hierarchy (default depth 4)

**Amends:** S1 (Directory), S2 (Access), `overview/visibility-matrix.md` §1–3. **Supersedes the earlier `01-org-model.md`**, which wrongly hardcoded Service/Bureau/Unit and their kinds.

## 0. The principle

The platform models **one self-referencing tree of org nodes, N levels deep (configured to 4)**. It does **not** know what the levels are called, what any node does, or what activities happen inside it. Those are per-deployment configuration.

> **Structural requirement (the actual ask):** every node has a head; a head supervises **the entire subtree beneath their node**; activity and reporting **roll up through every level** to the top. Depth, labels and semantics are data.

Concretely, the same schema serves:

| L1 | L2 | L3 | L4 |
|---|---|---|---|
| Services Généraux | Bureau IT | Pôle Dev | *(person)* |
| Service Bilatéral | Bureau Europe | *(no level 3)* | *(person)* |
| Direction Financière | Département Contrôle | Équipe Audit | *(person)* |
| *(any deployment's own words)* | | | |

**Nothing in the codebase may name a level.** No `bureau_id` column, no `service-head` role, no `switch` on level. Architecture test enforces it.

## 1. Schema (`directory`)

### Level configuration (per deployment, editable in Admin)
```sql
create table directory.org_level (
  level_no      int primary key check (level_no between 1 and 8),
  code          text not null unique,          -- 'L1','SERVICE','DIRECTION' — deployment's choice
  label_key     text not null,                 -- i18n key, singular  e.g. directory.level.service
  label_plural_key text not null,
  head_label_key   text not null,              -- 'Chef de service' / 'Chef de bureau' / 'Chef de pôle'
  people_allowed   boolean not null default true,  -- may a person attach directly at this level?
  is_optional      boolean not null default false  -- may a branch skip this level?
);
```
Seeded with 4 rows. **Depth is data**: adding a 5th level is a row + a label, not a migration. Code reads `max(level_no)`, never the literal 4.

### The node tree
```sql
create table directory.org_node (
  id            uuid primary key,
  parent_id     uuid references directory.org_node(id),   -- null only at level 1
  level_no      int  not null references directory.org_level(level_no),
  code          text not null,
  name          text not null,
  ancestor_ids  uuid[] not null,        -- materialized: [root … parent, self]. Maintained by trigger.
  head_person_id uuid,                  -- the supervisor of this subtree
  profile_id    uuid,                   -- activity profile (10), inherited from ancestors when null
  active        boolean not null default true,
  unique (parent_id, code)
);
create index on directory.org_node using gin (ancestor_ids);
create index on directory.org_node (parent_id, level_no);
```
- `ancestor_ids` **includes self** and is maintained by an `after insert/update of parent_id` trigger that recomputes the subtree. This is what makes every ancestry question a single indexed array operation instead of a recursive CTE inside an RLS policy (which would be a per-row disaster).
- Constraint: `child.level_no > parent.level_no` (strictly greater, not necessarily +1 — that's how **optional levels** work: a branch with no L3 attaches an L4-ish node, or people, directly to L2).
- Alternative implementation: `ltree`. `uuid[]` + GIN is chosen because node ids are uuids and array overlap (`&&`) expresses "is in any of my headed subtrees" in one operator.

### People
```sql
alter table directory.person
  add column home_node_id uuid not null references directory.org_node(id),
  add column node_ancestor_ids uuid[] not null;   -- denormalized copy of the node's path
```
A person attaches to **whatever level their branch actually has** — L3 where pôles exist, L2 where they don't. "Unit is optional" stops being a special case and becomes ordinary tree shape.

`PersonFunctionalRole` (dev, comptable, architecte, chargé de mission…) is unchanged from S1 and stays **orthogonal to the tree** — it describes the job, not the position in the hierarchy.

## 2. Roles

The role list collapses. Level-named roles are gone.

| Role | Scope | Meaning |
|---|---|---|
| **member** | implicit | Everyone. Sees own work + peers at their home node. |
| **node-head** | one or more `node_id` | Supervises that node's **entire subtree**, at any depth. A person may head several nodes. |
| **po** | item | Leads a portfolio item across nodes. |
| **pmo** | global | Portfolio governance. |
| **admin** | global or `node_id` | Platform admin (global) or delegated node admin (`08`). |

**One role replaces four.** "Chef de pôle", "chef de bureau", "chef de service" are the *same* role — `node-head` — at different depths; the difference in what they see falls out of the tree, not out of a role name. The visible **label** comes from `org_level.head_label_key`.

LDAP mapping (S1 sync): OU path → node path; a group or `manager` attribute → `node-head` on the matching node. The RBAC fallback (`08`) can assign `node-head` on any node when LDAP is incomplete.

## 3. RLS

### Session GUCs (replaces all earlier variants)
```
app.user_id · app.node_id · app.node_path (csv of ancestor uuids) · app.headed_nodes (csv) · app.roles
```
Note there is **no** `app.bureau_id` / `app.service_id`. One path replaces every level-specific id.

```sql
create function access.uid() returns uuid language sql stable as $$
  select nullif(current_setting('app.user_id', true),'')::uuid $$;

create function access.node() returns uuid language sql stable as $$
  select nullif(current_setting('app.node_id', true),'')::uuid $$;

-- ancestors of MY node (root … me): used for "is this above me?"
create function access.my_path() returns uuid[] language sql stable as $$
  select coalesce(string_to_array(nullif(current_setting('app.node_path', true),''), ',')::uuid[], '{}') $$;

-- the nodes I head (usually one, may be several)
create function access.headed_nodes() returns uuid[] language sql stable as $$
  select coalesce(string_to_array(nullif(current_setting('app.headed_nodes', true),''), ',')::uuid[], '{}') $$;

create function access.has(r text) returns boolean language sql stable as $$
  select r = any(coalesce(string_to_array(current_setting('app.roles', true), ','), '{}')) $$;

create function access.is_head() returns boolean language sql stable as $$
  select array_length(access.headed_nodes(), 1) > 0 or access.has('pmo') $$;
```

### The two predicates that carry the whole hierarchy

```sql
-- Is a row (carrying its node's ancestor_ids) inside a subtree I head? Depth-independent.
create function access.in_my_subtree(p_ancestors uuid[]) returns boolean language sql stable as $$
  select p_ancestors && access.headed_nodes();
$$;

-- Are we at the same home node? (peers — replaces "same unit or same bureau-direct")
create function access.same_node(p_node uuid) returns boolean language sql stable as $$
  select p_node = access.node();
$$;
```

`in_my_subtree` is the entire supervision rule. An L1 head's `headed_nodes` contains the L1 id, which appears in the `ancestor_ids` of **every** descendant row at L2, L3 and L4 — so one array-overlap gives a top-level head visibility over the whole tree beneath, an L2 head over their branch, an L3 head over their pôle, with no level logic anywhere.

### Scoped-row contract (replaces the old "must carry bureau_id + service_id")

> **Every scoped table carries `node_id uuid NOT NULL` and `node_ancestor_ids uuid[] NOT NULL`** (copied from the node at write time, refreshed by the re-parent trigger). RLS never joins upward.

### Predicate set

```sql
-- ── ACTIVITY ──────────────────────────────────────────────────────────────────
create function access.can_read_activity(
  p_owner uuid, p_node uuid, p_ancestors uuid[], p_item uuid)
returns boolean language sql stable as $$
  select p_owner = access.uid()
      or access.same_node(p_node)                       -- my peers
      or access.in_my_subtree(p_ancestors)              -- any head above me, at any depth
      or ((access.has('po')) and access.on_item(p_item))
      or access.has('pmo') or access.has('system');
$$;

-- ── PORTFOLIO ITEM ────────────────────────────────────────────────────────────
create function access.on_item(p_item uuid) returns boolean language sql stable as $$
  select p_item is not null and exists (
    select 1 from portfolio.item_member m
    where m.item_id = p_item and m.person_id = access.uid()
      and (m.to is null or m.to >= current_date));
$$;

-- an item touches a node I head (owner or any contributing node)
create function access.item_in_my_subtree(p_item uuid) returns boolean language sql stable as $$
  select exists (select 1 from portfolio.item_node inode
                 where inode.item_id = p_item
                   and inode.node_ancestor_ids && access.headed_nodes());
$$;

create function access.leads_item(p_item uuid) returns boolean language sql stable as $$
  select exists (select 1 from portfolio.item i
                 where i.id = p_item
                   and (i.lead_person_id = access.uid() or i.po_person_id = access.uid()))
      or access.item_in_my_subtree(p_item);   -- head of a contributing node
$$;

create function access.can_read_item(p_item uuid) returns boolean language sql stable as $$
  select access.has('pmo') or access.has('system')
      or access.on_item(p_item) or access.leads_item(p_item);
$$;
create function access.can_write_item(p_item uuid) returns boolean language sql stable as $$
  select access.has('pmo') or access.leads_item(p_item);
$$;

-- ── BUDGET (heads only, any level) ────────────────────────────────────────────
create function access.can_read_budget(p_ancestors uuid[]) returns boolean language sql stable as $$
  select access.has('pmo') or access.has('system') or access.in_my_subtree(p_ancestors);
$$;
create function access.can_write_budget(p_node uuid) returns boolean language sql stable as $$
  select access.has('pmo') or p_node = any(access.headed_nodes());  -- own node, not descendants'
$$;

-- ── PROBLEMS ──────────────────────────────────────────────────────────────────
create function access.can_read_problem(
  p_reporter uuid, p_node uuid, p_ancestors uuid[], p_category text)
returns boolean language sql stable as $$
  select p_reporter = access.uid()
      or access.same_node(p_node)
      or access.in_my_subtree(p_ancestors)
      or access.has('pmo') or access.has('system')
      -- nodes flagged as solution providers read solvable problems org-wide (see §5)
      or (p_category = any (select unnest(pr.solves_categories)
                            from directory.org_node n
                            join directory.node_profile pr on pr.id = n.profile_id
                            where n.id = any(access.headed_nodes())));
$$;

-- ── STRATEGY / MEETINGS ───────────────────────────────────────────────────────
create function access.can_read_objective(p_node uuid, p_ancestors uuid[], p_objective uuid)
returns boolean language sql stable as $$
  select access.has('pmo') or access.has('system')
      or access.node() = any(p_ancestors)          -- objective at or above my node → I read it
      or access.in_my_subtree(p_ancestors)
      or exists (select 1 from strategy.objective_contribution c
                 where c.objective_id = p_objective and access.on_item(c.item_id));
$$;
create function access.can_write_objective(p_node uuid) returns boolean language sql stable as $$
  select access.has('pmo') or p_node = any(access.headed_nodes());
$$;

create function access.can_read_meeting(p_nodes uuid[], p_ancestors uuid[], p_item uuid)
returns boolean language sql stable as $$
  select access.has('pmo') or access.has('system')
      or access.node() = any(p_nodes)              -- targeted at my node
      or access.my_path() && p_nodes               -- targeted at a node above me
      or access.in_my_subtree(p_ancestors)         -- I head a node at/above it
      or (p_item is not null and access.on_item(p_item));
$$;
```
`p_nodes` is an array so a meeting can target **several nodes at once** — that covers "cross-bureau", "cross-team", "two directorates", without a `level` enum. Cross-cutting meetings are just multi-node meetings.

### Discovery vs detail (unchanged intent, restated generically)
`can_read_item` gates the full identity card. A **discovery projection** (name · code · type · category · owning node · lifecycle · one-line summary) is readable by any authenticated user so duplicate work is findable across the whole tree; an inaccessible card offers *"Demander l'accès"* rather than a 403. Opt out per item with `confidential = true`.

## 4. Reporting rollup (the headline capability)

Because supervision is `in_my_subtree`, reporting composes **automatically at every level** with one recursive shape, not one report per level.

```
GET /api/reports/brief?nodeId={any node}&period=&depth=1|all
```
- Returns: the node's **own aggregate** (its directly-attached people) + **one child block per direct child node**, each already aggregated over *its* whole subtree.
- `depth=1` gives a head their immediate children (the normal COPIL view: an L1 head sees L2 blocks, not 400 people). `depth=all` expands.
- **Identical shape at every level.** An L3 brief and an L1 brief differ only in what's beneath them — so a head at any depth can hand their brief upward and it slots into their parent's report as one block. This is what makes "merge with other units for the n+1" work structurally instead of by copy-paste.
- Aggregation is **deterministic in code** (hours, counts, item states, budget); the on-prem LLM writes only the connective narrative (unchanged S8 rule).
- Default rendering is the **Brief** (whole hours, one headline line per child, top-3 + risks); the raw table stays under *Détails* (`07 §3`).

**Rollup invariant (test):** for any node, `sum(children subtree aggregates) + own direct aggregate == own subtree aggregate`. Assert at every level, including branches with skipped levels.

## 5. What replaces the "archetypes"

Activity taxonomies, board choices and hidden UI move to a **`node_profile`** attached to *any* node and inherited down the path (nearest ancestor with a profile wins, overridable at any depth). It is keyed by **nothing structural** — no level, no name. See the rewritten `10-node-profiles.md`.

## 6. Migration delta (amends `09`)

`09` Phases 1–3 change shape: instead of "add Service, rename Department→Bureau, make Unit optional", it becomes **one collapse into `org_node`**:
1. Create `org_level` (4 seeded rows, labels from the deployment) and `org_node`.
2. Insert existing departments as level-2 nodes, units as level-3 nodes, under a single seeded level-1 node (`À RECLASSER`); backfill `ancestor_ids` via the trigger.
3. Set `person.home_node_id` = old unit node, else old department node; backfill `node_ancestor_ids`.
4. Add `node_id` + `node_ancestor_ids` to every scoped table; backfill from the person/department; index GIN.
5. Swap predicates (one transaction per module) and re-`alter policy`.
6. Drop `department`/`unit` tables after the shim release.

Golden-diff gate is unchanged and is the stop-the-line signal: after step 5 the only diff must be heads gaining their full subtree.

## Tests
- **Unit:** `ancestor_ids` trigger on insert, re-parent (whole subtree recomputes), and cycle rejection; level constraint `child.level_no > parent.level_no`; skipped-level branches valid.
- **RLS matrix (rewritten fixture):** a tree of depth 4 with **one branch that skips level 3**. Assert: member sees own + same-node peers only; L3 head sees their pôle; L2 head sees all L3s beneath **and** the people attached directly at L2 in the skipped branch; L1 head sees everything beneath; nobody sees a sibling branch.
- **Depth-independence:** add a 5th level row and a node beneath; **no code change, no migration** — the same tests pass with one more level.
- **Architecture:** no identifier in the codebase matching `bureau|service|department|unit` as a structural concept; no literal `4` for depth.
- **Rollup:** the sum invariant in §4 at every node, including skipped-level branches.
- **E2E:** an L1 head opens a brief, sees one block per L2 child, drills into an L2 whose branch has no L3, and lands directly on people.
