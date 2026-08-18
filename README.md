# Department Activity & Portfolio Platform — Specification

An internal, **department-agnostic** platform to track people activity, projects, portfolio, scheduling and recognition. Born for IT (BUILD / RUN), but configurable so HR, Finance or any business unit can adopt it without code changes.

> **Positioning:** one deployment, one organization. "Versatility" is achieved through **per-department configuration** (activity taxonomies, role labels, board layouts) — *not* multi-tenancy.

---

## How to consume this spec (for Claude Code)

1. Read `overview/architecture.md` — the stack, module map, and cross-cutting concerns. **Read this first, always.**
2. Read `overview/conventions.md` — the mediator, the two module archetypes (2-layer CRUD vs DDD), testing strategy, i18n, errors. **This is binding for every slice.**
3. Read `overview/visibility-matrix.md` — the RLS + report visibility rules. Every slice that touches data references it.
4. Build the slices **in numeric order** (`slices/S0` → `slices/S11`). Each slice is independently shippable and lists its upstream dependencies. Do not start a slice until its dependencies are green (tests passing).

Each slice file follows the same skeleton: *Purpose → Depends on → Module archetype → Domain / Entities → API surface → RLS → Angular surface → Integrations → Tests → Acceptance criteria → Out of scope.*

---

## Build order (dependency-ordered)

| # | Slice | Archetype | Why it's here |
|---|-------|-----------|---------------|
| S0 | Platform foundation | infra | Host, mediator, EF+RLS scaffolding, BFF/Keycloak, storage, observability, test harness. Nothing works without it. |
| S1 | Directory (org backbone) | 2-layer + sync | People, Departments, Units (LDAP `fonction`), functional roles. Establishes the ambient user→unit→dept context RLS depends on. |
| S2 | Access & visibility | infra | Contextual roles, the visibility matrix, RLS policies. Consumed by every later slice. |
| S3 | Projects | DDD | Cross-department teams, manual cost, BUILD/RUN classification. |
| S4 | Portfolio | DDD | Lifecycle (considered → committed → active → déphasé), free iterations. |
| S5 | Activities | DDD | Configurable taxonomy, planned + actual slots, 35h guardrails. |
| S6 | Timeline views (Mobiscroll) | mixed | RUN work-orders, RUN shifts, BUILD task-progress + composed boards. |
| S7 | Meetings & events | 2-layer | Recurring meetings (weekly, copil), special days (patch party, audit). |
| S8 | Status reports + AI | DDD (orchestration) | Contextual-by-role report + on-prem LLM weekly/monthly summaries. |
| S9 | Kudos | DDD | Peer-to-peer, configurable counter/points/badges/leaderboard. |
| S10 | Integrations (read-only) | 2-layer sync | Azure DevOps + ServiceNow task pull. |
| S11 | Capex/Opex view | 2-layer query | Capitalization view for IT/department heads. |

---

## Glossary (fixed vocabulary — use verbatim in code & UI keys)

| Term | Meaning |
|------|---------|
| **Unit** | LDAP `fonction` grouping inside a department (e.g. Dev team, Helpdesk team). |
| **Functional role** | Job identity from LDAP (`dev`, `comptable`, `expert-comptable`, `architecte`, `tech-lead`, `chef-de-pole`). |
| **Contextual role** | Access-scoped role in the app (member, unit-head, dept-head, project-lead/PO, PMO). Resolved from LDAP groups with an in-app RBAC fallback view. |
| **BUILD** | Project delivery work (new capability). |
| **RUN** | Operational work — work-orders and shift-based rota. |
| **Considered** | Candidate project, not yet committed. |
| **Committed** | Approved, funded, not yet active. |
| **Active** | Running in production / in delivery. |
| **Déphasé** | Retired from production — **archived**. |
| **Iteration** | Free-length phase of a project with quick selectors (1w / 2w / 1m / custom). |
| **Kudo** | Peer-to-peer recognition for initiative / quality-of-life contribution. |
| **Copil** | *Comité de pilotage* — steering meeting (a recurring meeting type). |

---

## Non-negotiable technical constraints

- **Modular monolith**, vertical slicing, **no MediatR** — a hand-written in-process mediator (`overview/conventions.md`).
- CRUD modules = **2 layers** (FastEndpoints endpoint → service). Complex modules = **Clean Architecture DDD**.
- **PostgreSQL with Row-Level Security** as the primary enforcement of the visibility matrix.
- **Angular 22** with **Signals**, standalone components, **no external state management**.
- **BFF (YARP)** in front of the API; **Keycloak** (LDAP-synced) OIDC at the BFF.
- **RustFS** (S3-compatible) for object storage; **SEQ** for structured logs; **Prometheus/Grafana** for metrics.
- **On-prem, OpenAI-API-compatible** LLM for summaries (never a public endpoint).
- **Mobiscroll Angular** (licensed) for all timeline surfaces.
- **i18n: French, English, Spanish** — every user-facing string keyed, runtime-switchable.
- Tests: **unit + integration (Testcontainers / WebApplicationFactory) + architecture + Playwright e2e**.
