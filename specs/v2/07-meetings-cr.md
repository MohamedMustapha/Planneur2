# v2 · 07 — Meetings & Minutes (CR)

**Amends:** S7 (Meetings & Events) and the S8 report brief. Adds meeting **levels** (node / cross-node / service) and **compte-rendu (minutes)** with decisions and action items that link back to problems, projects and objectives — plus a meeting-ready report brief.

## 1. Meeting levels & CR

### Entity changes (`meetings` schema)
- `MeetingSeries` gains `level ∈ {unit, node, cross-node, service, project}` and `scope_ids[]` (cross-node meetings target several child nodes). `kind` extends with `copil`, `weekly-node`, `service-review`.
- **MeetingMinutes (CR)** `(id, occurrence_id, author_person_id, agenda, attendees_json, absentees_json, summary, published, published_at)`.
- **Decision** `(id, minutes_id, text, rationale, decided_by)`.
- **ActionItem** `(id, minutes_id, title, owner_person_id, due, status, link_type, link_id)`
  - `link_type ∈ {problem, item, objective, none}` → an action can point at a Problem (`05`), a PortfolioItem (`03`) or an Objective (`06`). Closing the linked thing can close the action.

## 2. Flow
- A recurring meeting (weekly node, COPIL, service review) generates occurrences (S7 RRULE). For any occurrence, the owner writes a **CR**: agenda, attendance, decisions, action items (each optionally linked), a short summary.
- **Publish** distributes the CR to the meeting's scope (node/cross-node/service) — visible on their dashboards' "Derniers CR" strip and included in reports.
- **Action tracking**: open action items roll up on the owner's board and on the node/service overview; overdue ones flagged. Linking to a problem/item/objective keeps the CR connected to real work, not a dead document.
- Optional **AI draft** of the summary from the deterministic agenda + linked-item states (on-prem LLM, S8 rules: numbers computed in code, narrative only).

## 3. The meeting-ready report brief (fixes the "synthèse")
The current per-unit table (decimals like 97,5 / QOL 0) is a raw consultation view, not something to present to your n+1 or to merge across units. Add a **Brief** rendering (default on the Reports page):
- **Whole-hour, rounded** figures; no decimals in the brief.
- One **headline sentence per unit/node** ("Études & Dev: 5 pers · ~98 h · 1 initiative QoL · RAS").
- **Top 3 items** (activity, project progress, risks) per scope; upcoming deadlines/COPIL.
- **Mergeable**: a node-head can stack unit briefs into a node brief; a node-head stacks node briefs into a service brief — same shape at each level, so it composes upward for a COPIL.
- The raw table stays available under "Détails" for solo consultation.

## 4. API surface
- S7 series endpoints gain `level`/`scope_ids`.
- `GET/POST/PATCH /api/meetings/occurrences/{id}/minutes` — CR CRUD.
- `POST …/minutes/{mid}/decisions`, `…/actions` (with link), `PATCH …/actions/{aid}` (status).
- `POST …/minutes/{mid}/publish`.
- `GET /api/meetings/actions?owner=me|scope&status=open|overdue` — action tracker.
- `GET /api/reports/brief?scope=unit|node|service&scopeId=&period=` — the meeting-ready brief (extends S8).

## 5. RLS
`access.can_read_meeting`/`can_read_minutes` by level scope: node CR to the node, cross-node CR to targeted child nodes, service CR (COPIL) to the service; action items visible to owner + scope; PMO all. Write/publish limited to the meeting owner's role.

## 6. Angular surface
- **Meetings** page: upcoming occurrences by level; "Écrire le CR" on an occurrence.
- **CR editor** (guided, big controls): agenda, attendance (from directory), decisions, action items with an entity-link picker (problem/item/objective), summary (+ AI draft).
- **Action tracker** on node/service overviews and on owners' boards; overdue badges.
- **Derniers CR** strip on dashboards; brief embeds strategy rollup (`06`).

## Tests (delta)
- **Unit:** action-item link resolution; brief rounding & composition (unit→node→service).
- **Integration:** CR RLS by level; publishing distributes to the right scope; action closes when linked item resolves.
- **E2E:** run a node weekly → write a CR with an action linked to a problem; the action appears on the owner's board; a node-head composes node briefs into a COPIL brief and exports PDF.
