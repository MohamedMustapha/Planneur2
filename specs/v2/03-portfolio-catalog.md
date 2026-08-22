# v2 · 03 — Portfolio, Catalog & Project Identity

**Amends:** S3 (Projects), S4 (Portfolio). Unifies "project" and "durable service/product" into one **portfolio item** so the portfolio is a complete, browsable map of everything the org runs or could run.

## 1. From "project" to "portfolio item"

A project is not only build/run/removed, and not only bespoke. Some items are **shared platforms** other items consume (a k8s cluster), some are **shelf products** operated as a service (SharePoint, a KM tool, messaging), some are **recurring public services** (subsidy selection & pay), some are **business initiatives** (a bilateral summit) or **intelligence production**. Model them all as `PortfolioItem` with a **type** and a **category**, so they coexist and can be filtered.

### Entity (`portfolio` schema — extends S3/S4)
- `PortfolioItem (id, code, name_key, type, category, classification, lifecycle_state, owner_node_id, , lead_person_id, po_person_id?, estimate_amount?, currency, awaiting_version?, summary, created/audit)`
  - **type** ∈ `{project, platform, product, run-service, business-initiative, intelligence}`
    - `project` — a bespoke build endeavor.
    - `platform` — a shared internal capability consumed by other items (k8s, CI, data lake).
    - `product` — an OOTB/COTS product operated as a service (SharePoint, messaging, KM).
    - `run-service` — a recurring operational public service (subsidies pay, audits campaign).
    - `business-initiative` — deals, relationships, summit or campaign goals.
    - `intelligence` — economic-intelligence production.
  - **category** — free but seeded per node profile (e.g. IT: `infra`, `collaboration`, `security`, `data`, `line-of-business`; Bilateral: `europe`, `aiea`, `asia`, `africa`). Editable in node config.
  - **classification** ∈ `{build, run, mixed}` stays as an orthogonal axis (a product can be `run`, a project `build`).
  - **lifecycle_state** ∈ `{considered, committed, active, awaiting-vnext, dephase}` — adds **awaiting-vnext** (live but a v2+ is queued).
  - **awaiting_version** — e.g. "v2" label when `awaiting-vnext`.
- `ItemMember (item_id, person_id, node_id, unit_id?, functional_role_id, allocation_pct?, from, to?)` — team, tagged by node→unit→function (the grouped project view).
- `ItemDependency (item_id, depends_on_item_id, kind)` — `kind ∈ {consumes, integrates, blocks}`. Lineage: *"Project X consumes Platform k8s."* Powers the catalog's "consumed by" and the "does something similar already exist?" search.
- **Iterations → Epics/Features** (extends S4 iterations):
  - `Iteration (id, item_id, sequence, name, length_preset {1w,2w,1m,custom}, starts_on, ends_on, state)`.
  - `Epic (id, item_id, iteration_id?, name, description, status {idea, planned, in-progress, done, deferred}, target_version?)` — the "features" of a v2. An item `awaiting-vnext` lists its deferred/planned epics as the v2 scope.

## 2. Two primary surfaces

### Catalog (default portfolio view — replaces the empty kanban as primary)
A browsable grid/list of **identity cards**, filterable by type, category, classification, lifecycle, owner node/service, and "shared/consumed-by-others". This is the *complete portfolio* any node consults to check if something similar exists before requesting a new build.

**Identity card (summary):** code · name · type & category chips · classification · lifecycle pill (incl. *Awaiting v2*) · owner node/service · lead/PO · team headcount by node · estimate & budget headline (`04`) · current iteration · #dependencies · linked objectives (`06`) & problems (`05`).

**Identity card (detail drawer/page):** the above plus full team grouped by node→unit→function, iterations timeline with epics, dependency graph (consumes / consumed-by), budget breakdown (`04`), linked strategy objectives, originating problem(s), transition/audit history, documents (RustFS).

### Flux (secondary tab — the old kanban)
Lanes by lifecycle (Envisagé · Engagé · Actif · **En attente v2** · Déphasé) with guarded drag transitions (S4 rules + the new state). Kept for governance, not the landing.

## 3. Create flow (fixes "can't create a project")

A **"Nouveau"** primary action on Projects/Portfolio opens a short **wizard**:
1. **Type & category** (project / platform / product / run-service / …; category from node config).
2. **Identity** — name, code (auto), owner node (defaults to creator's), lead/PO.
3. **Origin** (optional) — link an existing **Problem** (`05`) or **Objective** (`06`) this item answers; or "from scratch."
4. **Scope** — classification, first iteration length preset, initial epics (optional), dependencies (search the catalog → "consumes k8s").
5. **Estimate & budget** (optional at creation) — rough estimate; full budget in `04`.

Considered items can be created with only steps 1–2 (a candidate), matching the old "Proposer un candidat" but now producing a real identity card.

## 4. API surface (delta)
- `GET /api/portfolio/catalog?type=&category=&classification=&state=&sharedOnly=&owner=` — identity-card list, RLS-filtered.
- `GET /api/portfolio/items/{id}` — full identity card (team grouped, iterations+epics, dependencies, budget headline, links).
- `POST /api/portfolio/items` — create (wizard payload); `PATCH …/{id}` — edit.
- `POST /api/portfolio/items/{id}/dependencies`, `DELETE …`.
- `POST /api/portfolio/items/{id}/iterations` (+ epics), `POST …/epics`, `PATCH …/epics/{eid}`.
- `POST /api/portfolio/items/{id}/transition` — lifecycle incl. `awaiting-vnext`.
- `GET /api/portfolio/catalog/search?q=` — "does something similar exist?" (name, category, dependency, description).

## 5. RLS
`access.can_read_item(item)` = on the item's team, or a head whose node/service owns or shares it, or PMO. Catalog search is deliberately **broad-read for heads** (cross-node knowledge flow) so duplicates are discoverable; members see items they're on plus their node's. Writes: lead/PO of the item, owner node-head, PMO.

## 6. Angular surface
- **Catalog** page (default) with facets + identity cards; **detail drawer**. Type/category/classification/lifecycle chips use the new semantic tokens.
- **Create wizard** (4–5 steps, big controls, guided).
- **Iterations & epics** editor on the detail page (1w/2w/1m/custom; epics as feature list; "Awaiting v2" shows the queued epics).
- **Dependency view** (consumes / consumed-by) with catalog search.

## Tests (delta)
- **Unit:** type/category/lifecycle rules; awaiting-vnext requires ≥1 deferred/planned epic; dependency cycles rejected.
- **Integration:** catalog RLS (head cross-node read; member scoped); create wizard produces a card; search finds a similar item across sibling branches.
- **E2E:** create a `platform` item "k8s", create a `project` that "consumes k8s", see the dependency both ways; mark a live product `awaiting-vnext` with a v2 epic and see the pill + queued features.
