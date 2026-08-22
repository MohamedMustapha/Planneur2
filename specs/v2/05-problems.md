# v2 · 05 — Problems / Irritants (new · S12)

**New slice.** Anyone can file a problem — an irritant, a time-loss, a recurring pain — from work, a project, quality-of-life, or a tool gap. Others propose solutions. A node (esp. IT/Digital Transformation) triages and, when it's worth it, **converts the problem into a portfolio item run by IT** — "Claude-code-era" speed of production, but inside the IT department, **no shadow IT**.

## 1. Model (`problems` schema)

- **Problem** `(id, code, title, description, category, origin_scope_type, origin_scope_id, reporter_person_id, impact_time_loss, impact_frequency, affected_people_estimate, status, converted_item_id?, created/audit)`
  - `category ∈ {work-process, project, quality-of-life, tooling, data, other}`.
  - `origin_scope_type ∈ {person, unit, node, service, item}` — where it hurts.
  - `impact_time_loss` — e.g. hours/week lost; `impact_frequency ∈ {daily, weekly, monthly, occasional}`. Together they rank problems.
  - `status ∈ {new, triaged, accepted, converted, resolved, declined, duplicate}`.
  - `converted_item_id` — link to the `PortfolioItem` created to solve it (`03`).
- **Proposal** `(id, problem_id, author_person_id, description, effort_guess?, created_at)` — a suggested fix; multiple per problem.
- **ProblemVote** `(problem_id, person_id)` — "me too" / upvote, to surface high-impact pains. One per person.
- **ProblemComment** `(id, problem_id, author, body, created_at)` — discussion.

## 2. Lifecycle

```
new ──triage──> triaged ──accept──> accepted ──convert──> converted (→ PortfolioItem)
   └─> duplicate (link to existing)        └─> declined (with reason)
converted/accepted ──> resolved (when the item ships or the fix lands)
```
- **Triage** by a node-head / PMO in scope: set duplicate, decline (reason), or accept.
- **Convert**: an accepted problem becomes a portfolio item via the `03` create wizard **pre-filled** (title, description, origin = this problem, suggested category from `category`). The problem's `converted_item_id` links them; the item's card shows "Originating problem."
- **Duplicate check** on submit: search existing problems (and the catalog) so people find an existing pain or an existing product before filing/asking.

## 3. API surface
- `POST /api/problems` — file (with duplicate-search hint returned).
- `GET /api/problems?scope=&category=&status=&sort=impact|votes|recent` — RLS-filtered list/board.
- `GET /api/problems/{id}` — detail with proposals, votes, comments.
- `POST /api/problems/{id}/proposals`, `/vote`, `/comments`.
- `POST /api/problems/{id}/triage` (`{decision, reason?, duplicateOf?}`), `POST …/convert` (→ portfolio item), `POST …/resolve`.
- `GET /api/problems/search?q=` — dedup helper.

## 4. RLS
`access.can_read_problem(origin_scope, reporter)`: reporter always; peers in the origin unit/node; heads of the origin node/service; PMO; IT/Digital-Transformation node-heads get **cross-node read** on problems flagged `tooling|data|project` so they can pick up work (this is how other child nodes "fill in needs for IT"). Anyone in scope can propose/vote/comment; triage/convert limited to node-head / PMO (and the IT node for conversions).

## 5. Angular surface
- **Problems board** — columns by status or a ranked list by impact/votes; big "Signaler un problème" primary action.
- **Submit form** (guided, big controls): title, category, where it hurts, time lost, frequency; live duplicate suggestions.
- **Detail**: description, proposals (add yours), vote, comments; triage controls for heads; "Convert to project" for IT/PMO → opens the `03` wizard pre-filled.
- **My problems** and, for IT, an **incoming pipeline** view (accepted, not yet converted).
- Focus-mode target for members includes "Signaler un problème" as a one-tap action.

## Integrations
Emits `Problems.Converted` → Portfolio (`03`) creates/links the item; `Problems.Resolved` when the linked item ships. Appears as action-item source in meeting CRs (`07`) and as evidence under objectives (`06`).

## Tests
- **Unit:** impact ranking; lifecycle guards (can't convert a declined/duplicate); one vote per person.
- **Integration:** RLS (reporter + scope + IT cross-read; a foreign member can't see a node-internal problem); convert creates a linked portfolio item pre-filled.
- **Architecture:** DDD layering (this is a DDD module — lifecycle + rules).
- **E2E:** a helpdesk agent files a "too many manual ticket steps" irritant; a dev proposes a fix; the IT node-head triages → converts → a `project` item appears with the problem linked; resolving the item marks the problem resolved.
