# v2 · 00 — Reconciliation with v1 (READ FIRST)

**Purpose:** the v1 tree (`/specs/S0–S11`) and this pack disagree on names, on the org depth, and on what "a project" is. This file is the **arbiter**. Where v1 and v2 conflict, **v2 wins**. Read this before any other v2 file, and before re-reading any v1 slice.

> **Rule for Claude Code:** when a v1 slice mentions any term in the rename table below, substitute the v2 term. When a v1 section appears in the "void" column, ignore that section entirely and use the v2 file instead.

---

## 1. Global rename table (applies to every v1 file)

v1 modelled a 3-level org with named levels. v2 models **one generic node tree, N levels deep (configured to 4)**. Level names are per-deployment configuration, not schema.

| v1 term | v2 term | Notes |
|---|---|---|
| `Department`, `Unit` (as tables) | **`org_node`** (one self-referencing table) | Depth is data (`org_level`). Old departments become level-2 nodes, units level-3. |
| `department_id`, `unit_id` (FKs) | **`node_id`** + **`node_ancestor_ids uuid[]`** | Every scoped row carries both. RLS never joins upward. |
| `DepartmentConfig` | **`node_profile`** (attachable at any level, inherited) | See `10`. |
| `dept-head`, `unit-head` roles | **`node-head`** scoped to node id(s) | One role at every depth. Display label from `org_level.head_label_key`. |
| `app.dept_ids`, `app.unit_id` GUCs | **`app.node_id`, `app.node_path`, `app.headed_nodes`** | No level-specific GUC exists. |
| `access.can_read_*(… dept, unit …)` | predicates taking **`(node_id, ancestor_ids)`** | Full set in `01 §3`. |
| `Project` | **`PortfolioItem`** | Superset: project · platform · product · run-service · initiative · intelligence. |
| `project_id` | **`item_id`** | |
| `ProjectMember` / `ProjectDepartment` | **`ItemMember`** / **`ItemNode`** | Contributing **nodes**, one flagged lead. |
| "Department board" / "Unit board" | **"Node board"** (one board, any level) | Rows = child nodes, or people at a leaf. See §3. |
| Portfolio state set | unchanged **plus `awaiting-vnext`** | |

**Terms that no longer exist anywhere in code:** `service`, `bureau`, `department`, `unit` as structural identifiers. They may appear only as **seed data values** in `org_level.label_key`. Architecture test enforces this.

## 2. Slice-by-slice verdict

| v1 slice | Status | What is now void | Replaced by |
|---|---|---|---|
| **S0** Platform foundation | ✅ **Keep** | GUC list in §4 of architecture (dept_ids) | `01 §3` GUCs |
| **S1** Directory | 🔁 **Superseded** | the whole 3-level entity set (`Department`, `Unit`, `DepartmentConfig`) | `01 §1` (`org_level` + `org_node`), `10` (profiles) |
| **S2** Access | 🔁 **Superseded** | level-named roles; all predicate signatures | `01 §2–3`, `08` |
| **S3** Projects | 🔁 **Superseded** | the whole `Project` aggregate, `ProjectDepartment`, build/run-only axis | `03` (`PortfolioItem`) |
| **S4** Portfolio | ⚠️ **Amended** | kanban-as-primary; 4-state lifecycle; iterations without epics | `03` (catalog primary, 5 states, epics) |
| **S5** Activities | ⚠️ **Amended** | `project_id`/`department_id` columns; global taxonomy assumption | §3 below + `10` (per-bureau-kind taxonomies) |
| **S6** Timeline views | ⚠️ **Amended** | the five board names & scopes | §3 below |
| **S7** Meetings | ⚠️ **Amended** | scope enum without service/cross-bureau; no minutes | `07` |
| **S8** Reports | ⚠️ **Amended** | scope table (department); single report shape | `07 §3` (brief vs detail) |
| **S9** Kudos | ✅ **Keep** | only: surfacing kudos on team/board headers | `02 §3` (kudos lives on its own page) |
| **S10** Integrations | ⚠️ **Amended** | `ExternalConnection.department_id`; IT-only assumption | §3 below |
| **S11** Capex/Opex | 🔁 **Superseded** | entirely | `04` (consolidated finance) |
| — | ➕ **New** | — | `05` Problems (S12), `06` Strategy (S13), `08` Admin (S14) |
| `overview/visibility-matrix.md` | 🔁 **Superseded** | §1–4 and §6 | `01 §2–3`, `07 §3` |
| `overview/architecture.md` | ⚠️ **Amended** | module list, GUCs | §4 below |
| `overview/conventions.md` | ✅ **Keep** | nothing | — |
| `design/claude-design-prompts.md` (v1) | 🔁 **Superseded** | entirely | `design/claude-design-v2-prompts.md` |

---

## 3. Downstream deltas v2 implied but never wrote

Normative. All fall out of the generic tree.

### S5 Activities
- Columns: `person_id, node_id NOT NULL, node_ancestor_ids uuid[] NOT NULL, activity_type_id, item_id?, iteration_id?, epic_id?, kind, planned_*, actual_*, hours, source, external_ref, note`. No level-specific ids.
- Taxonomy resolution walks the **profile chain** (`10 §1`): nearest ancestor with a profile wins, field-level override allowed.
- The 35h guardrail is a profile capability (soft-warn default, hard-block optional).
- An entry may attach to an **epic**, so BUILD progress rolls to features.
- Validation: item-typed activity requires `ItemMember` on that item.

### S6 Boards — one board, parameterized by node
The five named boards collapse into **one**: `GET /api/boards?nodeId=&from=&to=`.
- If the node has **child nodes** → rows are the children (each aggregated over its subtree), drill-down on click.
- If the node is a **leaf** (or `expand=people`) → rows are the people attached there.
- `nodeId = my own node` gives the classic team board; `nodeId = a node I head` gives supervision at whatever depth; omitting `nodeId` gives *my week*.
- Item boards stay separate (`?itemId=`), rows grouped by contributing node → function.
- Which Mobiscroll archetype renders comes from the resolved profile (`10 §4`), not from the level.

### S8 Reports
- Scope becomes `me | node | item | portfolio` — **`nodeId` replaces the department/unit/service enum entirely**.
- Rollup shape and the `depth` parameter: `01 §4`.
- Two renderings per scope: **Brief** (default, meeting-ready) and **Detail** (`07 §3`).

### S10 Integrations
- `ExternalConnection.department_id` → **`node_id`**; connections attach to any node and are inherited down like profiles, so a branch can wire its own source without touching another's.
- Nodes whose resolved profile has `capabilities.integrations = false` expose **no** integration endpoints or UI.

### architecture.md
- Modules: `Portfolio` (merges v1 Projects + Portfolio), `Finance` (replaces S11), plus new `Problems`, `Strategy`, `Admin`.
- GUCs: `app.user_id · app.node_id · app.node_path · app.headed_nodes · app.roles`.

## 4. Conflict-resolution checklist (run before each delta slice)

1. Does the slice touch a renamed term? → apply §1.
2. Does it read a v1 section marked void in §2? → use the v2 file instead.
3. Does it write a scoped table? → it **must** carry `node_id` + `node_ancestor_ids`, or RLS in `01 §3` cannot evaluate.
4. Does it add UI? → it **must** declare its role visibility (`02 §1`) and its Focus-mode behavior (`02 §2`), or it will re-clutter the app.
5. Does it add a page for a capability some branches don't use? → it must be registered in the capability map (`10 §3`).
6. Does it name a hierarchy level anywhere (service/bureau/department/unit)? → **defect.** Use `node` + the level's configured label.
