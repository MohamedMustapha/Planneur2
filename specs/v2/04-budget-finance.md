# v2 · 04 — Budget & Finance (consolidated, drill-down)

**Replaces:** S11 (Capex/Opex). The v1 finance page is empty until you pick a project; v2 lands on a **consolidated view** and drills down. It adds licenses and external workers as first-class cost components and rolls everything up **recursively through the node tree**, at whatever depth the deployment has.

## 1. Model

### Entities (`finance` schema)
- **Budget** `(id, scope_type, scope_id, fiscal_year, planned_amount, currency)` — an envelope at `scope_type ∈ {node, item}`. **Any node at any level may carry a budget**; a parent's envelope is compared against the sum of its subtree, so "budget par direction / par département / par équipe" is one mechanism, not three.
- **CostComponent** `(id, item_id?, node_id?, kind, label, capex_or_opex, amount, currency, period_start, period_end, license_id?, external_worker_id?, notes)`
  - `kind ∈ {internal-effort, license, external-worker, cloud, hardware, service-fee, other}`.
  - `capex_or_opex ∈ {capex, opex, excluded}` — defaulted by `CapexOpexRule` (per node) but overridable per component.
- **License** `(id, node_id, product_name, vendor, seats, unit_cost, currency, billing_cycle, renewal_date, item_id?, active)` — e.g. SharePoint, an IDE, a SaaS. A license may attach to a product/platform item or sit at node level.
- **ExternalWorker** `(id, node_id, display_name, vendor, role, day_or_hour_rate, currency, contract_start, contract_end, item_id?, active)` — hired consultants. Their effort can also appear as `internal-effort`-style entries on boards (planned activity) while their **cost** lives here.
- **RateCard** `(id, node_id, functional_role_id, hourly_rate, currency, effective_from, effective_to?)` — values internal effort (from S5 hours) when present; else effort shows as hours only.
- **CapexOpexRule** `(id, node_id, build→, run→, qol→, admin→ : capex|opex|excluded)` — default BUILD→capex, RUN/QoL→opex, admin→excluded.

### Derived (materialized views, refreshed on write + nightly)
- `item_cost(item)` = Σ CostComponents(item) + valued internal effort (S5 hours × RateCard), split capex/opex.
- `node_own_cost(node)` = Σ item_cost(items owned by that node) + components attached directly to it (node-wide licenses, shared externals).
- `node_subtree_cost(node)` = `node_own_cost(node)` + Σ `node_subtree_cost(child)` — **one recursive definition covering every level**. Implemented as a single aggregate over `node_ancestor_ids && array[node]`, so it is one indexed scan rather than a recursion per level.
- Variance = subtree cost vs the node's own `Budget` envelope, computed at **every** node that has one.

**Rollup invariant (test):** `node_subtree_cost(n) == node_own_cost(n) + Σ node_subtree_cost(children(n))` at every node, including branches that skip a level.

## 2. Consolidated, drill-down UX (the key fix)

The Finance page **always shows data** at the highest node the viewer heads, and drills down the tree — never starts empty, never asks you to pick a project first.

```
<my highest headed node>   capex | opex | planned | variance
  └─ child node            (same columns)      ┐ repeats for as many levels
       └─ child node       (same columns)      ┘ as the deployment has
            └─ Portfolio item
                 └─ Cost component (license · external worker · effort · cloud · hardware)
```
- The number of intermediate rows is **whatever the tree depth is** — the UI recurses on children rather than rendering a fixed three-rung ladder.
- Every level shows the same four columns, so a head at any depth reads the same table and can hand it upward.
- Toggles: capex-only / opex-only / both; include effort cost (rate card) on/off; fiscal year; planned-vs-actual variance.
- **Licenses** and **External workers** tabs (renewal and contract-end calendars) attach at any node or item and feed the roll-up from wherever they sit.

## 3. API surface
- `GET /api/finance/consolidated?scope=service|node|item&scopeId=&fy=&mode=capex|opex|both&withEffort=true` — the drill-down node (totals + children).
- `GET /api/finance/items/{id}/components`, `POST/PATCH/DELETE …/components`.
- `GET/POST/PATCH/DELETE /api/finance/licenses`, `…/external-workers`, `…/budgets`, `…/rate-cards`, `…/rules`.
- `GET /api/finance/licenses/renewals?within=90d`, `…/external-workers/expiring?within=90d`.
- `GET /api/finance/consolidated/export?format=xlsx` — via the xlsx skill → RustFS.

## 4. RLS
`access.can_read_budget(node, service, item)` — node-head reads their node (all items + components); node-head reads their whole service (read); PMO reads all; members/POs **denied** the finance module (as v1). Writes: node-head for their node's budgets/components/licenses/externals; node-head sets service envelopes; PMO governance.

## 5. Angular surface
- **Consolidated dashboard** (default, never empty): tree/table with capex|opex|planned|variance columns, expandable rows, breadcrumb drill. Charts: capex vs opex per node/item; top cost drivers; license & external-worker spend.
- **Licenses** tab: table + renewal calendar; attach to item/node.
- **External workers** tab: table + contract-end calendar; attach to item; optional link to their planned activity on boards.
- Editors big-control, guided; export to Excel.

## Tests (delta)
- **Unit:** capex/opex rule application + per-component override; effort valuation with effective-dated rate cards; roll-up math service←node←item←component; variance.
- **Integration:** consolidated endpoint returns non-empty at each scope; RLS (node-head only own node; node-head service read; member 403); renewal/expiry windows correct.
- **E2E:** node-head adds a SharePoint license + a consultant, sees them roll into the node opex/capex and into the item's card (`03`); node-head sees the consolidated service total and drills to the component; export xlsx.
