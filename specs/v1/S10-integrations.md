# S10 — Integrations (read-only pull)

## Purpose
Pull work items **read-only** from **Azure DevOps** (BUILD / sprint tasks for devs) and **ServiceNow** (RUN / incidents & requests for helpdesk) so users can select them into activities (S5) and fill the RUN work-order pool (S6a). **No write-back** — the platform never mutates the external systems in v1.

## Depends on
S0–S3 (S5 consumes the assignable tasks; S6 consumes the pool).

## Module archetype
**2-layer sync.** Adapters + a mirror read model; scheduled + on-demand. Runs under `system` context.

## Domain / Entities (`integrations` schema — mirror only)
- **ExternalConnection** `(id, department_id, provider, base_url, auth_ref, project_or_queue, poll_interval, active)` — `provider ∈ {azure-devops, servicenow}`. Credentials in vault, referenced by `auth_ref`.
- **ExternalWorkItem** `(id, provider, external_id, connection_id, title, type, state, assigned_to_ldap_uid?, sprint_or_queue, project_id?, url, updated_at_source, synced_at)` — the read-only mirror. Mapped to a local `project_id`/`unit` where derivable.
- Mapping tables: DevOps area/iteration → local project; ServiceNow assignment group → local unit.

## Sync behavior
- Poll each active connection on its interval (and on-demand endpoint). Upsert by `(provider, external_id)`; mark stale items closed. Idempotent; failures/latency to Prometheus; last-sync gauge per connection.
- **Assigned-to-me** resolution: match `assigned_to_ldap_uid` to a Person; this powers S5's "assigned to me" dropdown and S6a's pool (unassigned ServiceNow items for the unit's queue).
- Read-only guarantee enforced: adapters expose **no** write methods; an architecture test forbids any HTTP verb other than GET/POST-for-query against provider clients.

## API surface
- `GET /api/integrations/connections`, `POST/PUT/DELETE` (dept-head/PMO) — configure connections & mappings.
- `POST /api/integrations/{id}/sync` — trigger pull (`202`).
- `GET /api/integrations/work-items?provider=&assignedToMe=true|false&sprintCurrent=true|false&unassigned=true` — the mirror, RLS-filtered; consumed by S5 (`assignable-tasks`) and S6 (`work-orders/pool`).

## RLS
Mirror rows scoped by `department_id`/derived `project_id`/`unit`: a dev sees DevOps items assigned to them or in their project's current sprint; a helpdesk agent sees ServiceNow items in their unit's queue; heads see their department's items; PMO all. Connection config visible/editable to dept-head/PMO only.

## Integrations (external, read-only)
- **Azure DevOps**: REST — work items by area/iteration, current sprint, assigned-to. PAT/OAuth from vault.
- **ServiceNow**: Table API — incidents/requests by assignment group / assigned-to. Basic/OAuth from vault.
- Both on-prem-reachable or via approved egress; endpoints allow-listed.

## Tests
- **Unit:** DTO mapping (DevOps/ServiceNow → ExternalWorkItem); assigned-to resolution; stale-close logic.
- **Integration:** sync against **recorded/stub** provider responses (no live calls in CI); idempotency; RLS (dev sees own DevOps items, not a foreign unit's ServiceNow queue).
- **Architecture:** read-only rule (no external write verbs); 2-layer isolation.
- **E2E:** configure a stub DevOps connection, sync, then in S5 a dev sees the pulled sprint task in the dropdown; configure a stub ServiceNow connection, sync, then in S6a the tickets appear in the work-order pool.

## Acceptance criteria
- Azure DevOps and ServiceNow items mirror read-only, mapped to local projects/units, refreshed on schedule + on demand.
- "Assigned to me / current sprint" and "unassigned queue" feeds power S5 and S6a.
- No write-back path exists (proven by architecture test).

## Out of scope
Any write-back. Other providers (Jira, Git) — future connections reuse `ExternalConnection`.
