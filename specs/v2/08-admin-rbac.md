# v2 · 08 — Administration & RBAC (new · S14)

**New slice** (amends S2). Two admin surfaces — **global (run by IT)** and **per-node** — with RBAC. **Team-member management moves here** out of the boards (you flagged the boards shouldn't own it).

## 1. Two scopes of admin

### Global admin (IT / it-global-admin)
Operates the platform for the whole org:
- **Org structure:** define the **levels** themselves (`org_level`: how many, their labels, their head titles, whether people may attach there) and edit the node tree — create, rename, re-parent, deactivate nodes at any depth. Re-parenting recomputes `ancestor_ids` for the whole subtree, which re-scopes RLS instantly. Run/inspect LDAP sync; resolve the migration placeholder node.
- **People:** global directory, deactivate/reactivate, fix node/unit assignment when LDAP is wrong.
- **RBAC (global):** grant/revoke `node-head` on any node, and `po`/`pmo`/`admin`; view effective roles; manage RBAC fallback overrides org-wide. There is **one head role**, so granting is 'who heads which node', not a matrix of level-specific roles.
- **Profiles (`10`):** author/clone node profiles (taxonomy, boards, capabilities, budget defaults, headline pattern) and attach them to nodes; preview the resulting week grid and brief before saving.
- **Integrations:** Azure DevOps / ServiceNow connections & mappings (S10), LLM endpoint, storage.
- **Feature flags & theme defaults:** Focus-mode default per role, brief-vs-detail default, etc.

### Node admin (delegated to a node-head)
Operates one node **and everything beneath it**:
- **Members:** add/move people into the node and its units, set unit-heads, set functional roles (within LDAP-allowed set + overrides). This is the **team-member management** UI, scoped to the node.
- **Node config:** activity taxonomy overrides, project categories, kudo mode, board layout, meeting cadences.
- **Budget config:** rate cards, capex/opex rules, licenses & external workers (`04`) — node-scoped.
- **RBAC (subtree):** grant `node-head` on **descendant** nodes only, plus PO on their items; overrides expire and are audited. A head can never grant at or above their own node.
- **Strategy:** manage the node strategy (`06`) entry points.

## 2. RBAC model (extends S2)
- Contextual roles: `member, unit-head, po, pmo, node-head, node-head, it-global-admin` at scopes `unit|node|service|item|global`.
- **Effective role = LDAP-derived ∪ active RBAC overrides**, deny beats grant, overrides expire. Global admin can act at any scope; node-head only within their node; node-head read across their child nodes, grant service-scoped roles.
- Every grant/revoke/re-parent/member-move is **audited** (who, when, why). Admin actions run under the actor's context (not `system`) so RLS + audit apply.

## 3. API surface
- **Global:** `GET/POST/PATCH /api/admin/services|child nodes|units`, `…/people`, `…/rbac`, `…/taxonomy-defaults`, `…/integrations`, `…/feature-flags`, `POST /api/admin/sync`.
- **Node:** `GET/POST/PATCH /api/admin/node/{id}/members` (add/move/roles), `…/config`, `…/budget-config`, `…/rbac`, `…/strategy`.
- `GET /api/admin/audit?scope=&type=&from=&to=` — the admin audit trail.

## 4. RLS
Admin endpoints gated by policy **and** RLS: `it-global-admin` → everything; `node-head` → only their node's admin rows; `node-head` → read across their service + grant service-scoped roles. Members/POs have no admin access. Reparenting/role-grant beyond scope → 403.

## 5. Angular surface
- **Admin (global)** — sections for Org, People, RBAC, Taxonomy, Integrations, Flags, Audit. Guided, big-control forms; destructive actions confirmed.
- **Admin (node)** — Members (the moved team-management UI: add/move person, set unit & functional role), Config, Budget config, RBAC, Strategy. Node-head lands here from their overview's "Admin" nav.
- Clear "source: LDAP vs override" badges on roles; audit visible inline.

## Tests
- **Unit:** effective-role merge with overrides/expiry; scope guards (node-head can't grant outside node).
- **Integration:** admin RLS (node-head sees only own node; global admin all); member-move updates directory + re-scopes RLS for that person; every action audited.
- **Architecture:** admin actions never bypass RLS via `system`.
- **E2E:** global admin re-parents a migrated node under the right service; a node-head adds a new dev to a unit and grants a temporary unit-head override; both appear in the audit trail.
