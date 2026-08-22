# v2 — Evolution Pack

This pack **amends** the original `/specs` tree (S0–S11). It does not replace it; each file states which original slices it changes. Build order for the delta is at the bottom.

## Why v2

The v1 app is organized around *entities* (one page per thing) and a 3-level org (Department → Unit → Person). Two problems fall out of that:

1. **Wrong shape of org.** Real structure is **Service → Bureau → Unit(optional) → Person**, and people belong administratively to a *Bureau*, not a unit. Units are an optional sub-fraction. The tool must read at four levels.
2. **Feature-first, not intent-first.** Every role sees every page and every page shows everything. A dev fills a week; a service-head reviews bureaux and runs a COPIL — they should not land on the same cluttered screen. v2 makes navigation and page content **follow the viewer's intent**, with a **Focus mode** that strips everything but the primary task.

It also adds the capabilities v1 lacked: a real **portfolio with project identity cards and a service catalog**, **budget/finance consolidation** (opex/capex, licenses, external workers), **Problems/Irritants intake** feeding an IT project pipeline, a **Strategy → Objectives → Projects** spine, **meeting minutes (CR)** at bureau/cross-bureau/service level, and proper **admin (global + per-bureau) with RBAC**.

## Files in this pack

| File | Amends / adds | Summary |
|---|---|---|
| `00-reconciliation.md` | **arbitrates v1 vs v2** | **Read first.** Rename table, slice-by-slice verdict, downstream deltas (S5/S6/S8/S10). |
| `01-org-model.md` | **supersedes S1, S2, visibility-matrix** | **Generic N-level node tree (depth 4 by default)**; one `node-head` role; path-based RLS; rollup at every level. |
| `02-navigation-focus-mode.md` | **cross-cutting (amends S6 shell, all Angular)** | Role→intent navigation map, Focus mode, declutter rules, theme intent. |
| `03-portfolio-catalog.md` | **amends S3, S4** | Portfolio-item types & categories, service catalog, lineage, iterations→epics, identity card, create wizard. |
| `04-budget-finance.md` | **replaces S11** | Consolidated drill-down (service→bureau→item→component), opex/capex, licenses, external workers. |
| `05-problems.md` | **new — S12** | Irritants/problems → proposals → convert to portfolio item (IT-run, no shadow IT). |
| `06-strategy-objectives.md` | **new — S13** | Strategy → measurable objectives → contributing projects; rollup; COPIL review. |
| `07-meetings-cr.md` | **amends S7** | Meeting levels + minutes (CR): decisions, action items linking to problems/projects/objectives. |
| `08-admin-rbac.md` | **new — S14 (amends S2)** | Global admin (IT) + per-bureau admin; member management moved here; RBAC. |
| `09-migration-runbook.md` | **operational** | Phased, reversible v1→v2 DB + code migration with golden-diff gates. |
| `10-node-profiles.md` | **amends S1, S5, S6, S8** | Profiles attachable to any node and inherited down: taxonomy, boards, capabilities, budget defaults. |
| `design/claude-design-v2-prompts.md` | **replaces the v1 design pack** | New theme (bigger controls, brighter, leaner, guided) + Focus mode + every new screen. |

## Delta build order

**Step 0 — read `00-reconciliation.md`.** Without it, an agent consuming the v1 tree will build a contradictory schema.

Then: org model first (everything scopes off it), capability slices next, re-shell last.

| # | File | Why here |
|---|---|---|
| 0 | `00-reconciliation` | Arbitrates every naming conflict with v1. Not code — required reading. |
| 1 | `01-org-model` + `09-migration-runbook` Ph.1–4 | Directory + Access + RLS to 4 levels, executed as reversible phases. **Blocking.** |
| 2 | `10-bureau-archetypes` | Seeds taxonomies/boards per bureau kind. Before Activities changes, or you re-seed twice. |
| 3 | `03-portfolio-catalog` | Item types, catalog, epics, create wizard. |
| 4 | `04-budget-finance` | Consolidation on top of the catalog. |
| 5 | `05-problems` | Intake pipeline (converts into `03` items). |
| 6 | `06-strategy-objectives` | Strategy spine (links `03` + `05`). |
| 7 | `07-meetings-cr` | Minutes reference items, problems, objectives — so it comes after them. |
| 8 | `08-admin-rbac` | Global + bureau admin; enables Phase 6 re-parenting/archetype assignment. |
| 9 | `02-navigation-focus-mode` + design pack | **Re-shell last** (`09` Ph.7): one coherent visual change instead of six confusing ones. |

> `02` moved from second to last on purpose. Re-shelling before the entities settle means building the nav twice — and users experience the redesign as churn rather than a relaunch.

## Decisions taken (previously open questions)

These were flagged as open; defaults are now **decided and specified**. Override any by saying so — each is a localized change.

| Question | Decision | Where | To reverse |
|---|---|---|---|
| How are the 4 levels modelled? | **One generic `org_node` tree.** Level count, labels and head titles are configuration (`org_level`); the code never names a level and never hardcodes depth 4. | `01 §0–1` | None needed — adding/removing a level is a config row. |
| Are PO/PMO org nodes? | **No — cross-cutting item roles.** A person can be a unit member *and* PO of an item in another bureau. | `01 §2` | Add `po_person_id` to Unit and gate by org node instead. |
| Who may convert a Problem into a project? | **IT / digital-transformation bureau-heads + PMO** (no shadow IT), but any bureau-head may convert a `quality-of-life` problem into a **run-service** item they own. | `05 §4`, `01 §3` | Widen `can_read_problem`'s last clause and the convert policy. |
| Can everyone see every project? | **Discovery yes, detail no.** A narrow discovery projection (name/type/category/owner/state) is visible org-wide so duplicates surface; full identity cards follow `can_read_item`. Opt-out via `item.confidential`. | `01 §3.1` | Set `can_discover_item = can_read_item`. |
| Focus mode default? | **On for members, off for heads/PO/PMO**, server-persisted, always toggleable, pierced only by obligations. | `02 §2` | Change the seeded flag in `08`. |

## Standing invariants (do not violate in any slice)

- Every scoped table carries `node_id` + `node_ancestor_ids` so RLS never joins upward and never recurses.
- No module hand-rolls a role check — only `access.*` predicates from `01 §3` (architecture test).
- No `switch` on bureau kind anywhere — archetype behavior is data (`10 §6`).
- AI writes narrative only; every number is computed in code (unchanged from v1 S8).
- RLS is never disabled for a migration (`09`).
- Kudos stays as v1 S9 — v2 only stops surfacing it where it clutters (`02 §3`).
