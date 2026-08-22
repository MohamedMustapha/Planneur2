# v2 · 10 — Node Profiles (configurable behavior, no hardcoded semantics)

**Amends:** S1/S5 (taxonomy), S6 (board selection), S8 (report vocabulary), `02` (UI hiding). **Replaces `10-bureau-archetypes.md`**, which wrongly keyed behavior to level names and organizational kinds.

## 0. The correction

The previous version defined behavior per `Bureau.kind ∈ {it, business, intelligence, …}`. That is the same mistake as naming the levels: it assumes the deployment's org semantics. A **profile** instead is a free-standing configuration row that can be attached to **any node at any level**, and inherited downward.

> A node does not "have a kind." A node **points at a profile**, or inherits the nearest ancestor's. Two nodes at different levels in different branches may share one profile; a single node may override one field of it.

## 1. Schema (`directory`)

```sql
create table directory.node_profile (
  id uuid primary key,
  code text unique not null,               -- deployment's own words: 'DELIVERY','CASEWORK','ADVISORY'…
  label_key text not null,
  activity_taxonomy jsonb not null,        -- buckets → subtypes (codes + i18n keys)
  board_archetypes  text[] not null,       -- subset of {week-grid, work-order, shift, task-progress}
  item_types        text[] not null,       -- subset of the portfolio item types (03)
  capabilities      jsonb not null,        -- feature switches, see §3
  solves_categories text[] default '{}',   -- problem categories this node accepts org-wide (05)
  budget_defaults   jsonb not null,        -- capex/opex treatment per bucket (04)
  headline_pattern  text                   -- report brief sentence template (§5)
);
-- org_node.profile_id → node_profile.id, NULL = inherit
```

### Resolution (normative)
```
effective_profile(node) = nearest ancestor of node (self first) with profile_id not null
effective_field(node, f) = first non-null f walking self → root
```
So an L1 can set a default for a whole branch, an L2 can override the taxonomy, an L3 can override only the board — without any of them knowing what level they are.

## 2. Universal buckets, configurable subtypes

The **four top buckets are fixed platform-wide** so rollup (`01 §4`) composes across dissimilar branches:

`project-build` · `project-run` · `quality-of-life` · `recruitment-admin`

Everything below is profile data. A delivery-flavored profile might define `dev / architecture / testing / deployment` and `incident / request / patching / on-call`; a casework profile `file-review / eligibility-check / payment-run / appeals`; an advisory profile `research / analysis / note-production / watch`. **The platform ships example profiles as seed data, not as classes** — an admin can delete all of them and author their own without a code change.

Why the fixed top layer: an L1 head's brief stacks a delivery branch's incidents and a casework branch's processed files under the same four headings. Without it, cross-branch rollup is meaningless.

## 3. Capabilities (what a node's UI shows)

```jsonc
"capabilities": {
  "integrations": false,      // hides the import dropdown ENTIRELY (not rendered-and-empty)
  "shift_scheduling": false,
  "work_order_pool": false,
  "task_progress": true,
  "kudos": true,
  "budget": true,
  "strategy": true
}
```
Consumed by `02 §1` nav hiding and by each board. **Rule: a capability that is off means the control is absent, not disabled** — a person in an advisory branch must never see an integration import they can't use.

## 4. Board selection

`board_archetypes` picks from the three Mobiscroll archetypes plus the plain grid. Selection is **per profile, not per level** — an L3 pôle doing dispatch gets `work-order`, its sibling L3 doing delivery gets `task-progress`, both under the same L2.

## 5. Report vocabulary

`headline_pattern` is a template rendered with deterministic counts, e.g.
`"{people} pers · ~{hours} h · {c1} {c1_label} · {c2} {c2_label} · risques : {risks}"`
where `c1/c2` bind to profile-chosen activity subtypes or item states. The LLM receives the resolved profile label and vocabulary as a hint and writes only connective narrative — numbers stay computed in code (S8 rule, unchanged).

## 6. Authoring profiles

Admin UI (`08`): create/clone a profile, edit taxonomy and capabilities, attach to nodes, preview the resulting week grid and brief. Cloning is the expected path — a new branch copies a close profile and edits.

**Architecture tests:**
- No `switch`/`if` on a profile `code` anywhere — profiles are data end to end.
- No identifier in the codebase matching organizational level names as a structural concept (shared with `01`).
- Every UI control that a capability can hide is registered in one capability map (so a new page can't silently bypass hiding).

## Tests
- **Unit:** profile resolution walking self→root, per-field override, inheritance across a skipped level.
- **Integration:** a node whose profile has `integrations:false` returns 404/empty on integration endpoints and omits them from nav; posting an activity subtype absent from the resolved taxonomy is rejected.
- **E2E:** build a 4-level tree where two sibling branches use different profiles; each member sees only their branch's subtypes and controls; the L1 brief stacks both branches under the same four universal buckets.
- **Configurability:** author a brand-new profile from the admin UI with invented subtypes and attach it mid-tree — no deployment, no migration.
