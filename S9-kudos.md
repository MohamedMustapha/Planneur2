# S9 — Kudos

## Purpose
Peer-to-peer recognition for initiative and quality-of-life contributions (cleanups, improvements, mentoring…). Configurable per department by the manager: a plain **counter** by default, optionally **points / badges / leaderboard**. Kudos totals surface **monthly on the team board** and are meant to feed **annual-review claims**.

## Depends on
S0–S3 (S1 department config for rules; S6 team board to display).

## Module archetype
**DDD.** The award rules, anti-abuse invariants, and configurable scoring make it more than CRUD.

## Domain (`kudos` schema)
**Aggregate: Kudo**
- `Kudo (id, from_person_id, to_person_id, unit_id, department_id, category, message, points, created_at)`
  - `category` from department config (e.g. `initiative`, `cleanup`, `improvement`, `mentoring`, `above-and-beyond`).
  - `points` derived from the department's `KudoRules` (0 in pure-counter mode).
- **KudoRules** (per department, from `DepartmentConfig`) `(mode, category_points_json, monthly_cap_per_giver, self_kudo_allowed=false, badges_json)`
  - `mode ∈ {counter, points, points-badges-leaderboard}`.
- **Badge** `(id, code, label_key, criteria)` and **PersonBadge** `(person_id, badge_id, awarded_at)` — awarded when thresholds met (points mode).
- Domain events: `KudoGiven`, `BadgeAwarded`.

**Invariants**
- No self-kudos. Giver and receiver must share a **unit** (members can kudo unit peers — the reason members can see their unit in the matrix) or a **project team**; heads may kudo within their scope.
- `monthly_cap_per_giver` prevents inflation.
- Points/badges only when the department mode enables them; otherwise it's a bare counter.

## API surface
- `POST /api/kudos` — give a kudo (category + message) to an eligible peer.
- `GET /api/kudos?scope=me|unit|department&period=month|year` — received/given, RLS-filtered.
- `GET /api/kudos/leaderboard?scope=unit|department&period=` — only if the scope's department enables leaderboard mode.
- `GET /api/kudos/me/annual` — the annual-review claim view (all kudos received in a year, grouped by category, with messages).
- Department rules edited via S1 `DepartmentConfig` (`kudo_rules_ref`).

## RLS
`access.can_read_kudo(unit_id, department_id, to_person, from_person)`: a person sees kudos they gave or received, kudos within their unit, and (heads) within their department; PMO all. Giving is validated against the shared-unit/project eligibility rule. Leaderboard visibility gated by department mode.

## Angular surface
- **Give-kudo** action available from a teammate's card / activity row (member sees unit peers → can kudo them).
- **Kudos wall** per unit; **monthly totals on the team board** (integrates with S6/S8 board area).
- **Leaderboard** shown only when the department enabled it; otherwise a simple per-person counter.
- **My annual kudos** export (for review claims).

## Integrations
Consumes `DepartmentConfig` (S1) for rules; emits `Kudos.KudoGiven` for board/report refresh. Monthly rollup shown on the team board and included in unit/department reports (S8).

## Tests
- **Unit:** eligibility (no self, must share unit/project), monthly cap, points derivation per mode, badge threshold awarding.
- **Integration:** give/read incl. RLS (member sees unit kudos, not a foreign unit's; leaderboard hidden when mode=counter); annual view aggregates correctly.
- **Architecture:** DDD layering.
- **E2E:** in a department set to counter mode, a member gives a cleanup kudo to a unit peer → appears on the team board's monthly total; switch the department to points-badges mode → leaderboard and a badge appear.

## Acceptance criteria
- Peer-to-peer kudos with per-department configurable mode (counter → points → badges+leaderboard).
- Eligibility + monthly cap prevent abuse; no self-kudos.
- Monthly totals show on the team board; annual claim view available.

## Out of scope
Monetary rewards. Cross-org social feed. Manager approval workflow (kudos are direct per your note).
