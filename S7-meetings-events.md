# S7 — Meetings & Events

## Purpose
Configurable **recurring meetings** (weekly stand-up, copil/steering, etc.) and **special days / deadlines** (patch party, audit, go-live) that surface on the boards — chiefly the **department view** — and feed the status reports.

## Depends on
S0–S3 (S6 to overlay onto boards; S8 to appear in reports).

## Module archetype
**2-layer CRUD.** Recurrence + targeting is the only nuance; no domain lifecycle.

## Domain / Entities (`meetings` schema)
- **MeetingSeries** `(id, kind, name_key, scope_type, scope_id, recurrence_rule, duration_min, owner_person_id, location, video_link, active)`
  - `kind ∈ {weekly, copil, retro, one-on-one, custom}` (extensible per department config).
  - `scope_type ∈ {unit, department, project, org}`; `scope_id` targets who sees/attends.
  - `recurrence_rule` = iCal RRULE (weekly/monthly/etc.); materialized into occurrences on demand.
- **MeetingOccurrence** `(id, series_id, starts_at, ends_at, status, notes_ref?)`.
- **SpecialDay** `(id, kind, name_key, scope_type, scope_id, date, all_day, severity, description)`
  - `kind ∈ {patch-party, audit, go-live, deadline, freeze, holiday, custom}`; `severity` drives board styling.
- **Attendance** (optional) `(occurrence_id, person_id, response)` for copil-type meetings.

## API surface
- `GET/POST/PUT/DELETE /api/meetings/series` — manage recurring meetings (scope-owner: unit-head/dept-head/PO/PMO).
- `GET /api/meetings/occurrences?from=&to=&scope=` — materialized occurrences for boards/reports.
- `GET/POST/PUT/DELETE /api/meetings/special-days` — patch party, audit, deadlines.
- `POST /api/meetings/occurrences/{id}/respond` — attendance.

## RLS
`access.can_read_meeting(scope_type, scope_id)`: a person sees series/special-days targeting their unit, a department they belong to, projects they're on, or org-wide; heads see their scope; PMO all. Writes limited to the scope owner's role.

## Angular surface
- **Meeting manager**: create a weekly or copil series with RRULE picker, scope target, video link; list upcoming occurrences.
- **Special-day manager**: add patch party / audit / deadline with date, severity, scope.
- These render as **overlays on S6 boards** (marked columns/badges, especially the department view) and as a **"coming up" strip** on dashboards.

## Integrations
Emits `Meetings.SpecialDayUpserted` / `MeetingScheduled` so Scheduling overlays refresh and Reporting can include "upcoming deadlines" and "meeting cadence."

## Tests
- **Unit:** RRULE expansion to occurrences; scope targeting.
- **Integration:** CRUD incl. RLS (a member sees a department-wide audit day but not another unit's private meeting); occurrence materialization windowed correctly.
- **Architecture:** 2-layer isolation.
- **E2E:** create a weekly copil (department scope) and a patch-party special day → both appear on the department board and in the "coming up" strip for members of that department.

## Acceptance criteria
- Recurring meetings (weekly, copil, …) are configurable with proper recurrence and scope.
- Special days/deadlines (patch party, audit, …) surface on boards (esp. department view) with severity styling.
- Everything is RLS-scoped and available to reports.

## Out of scope
Full calendaring/RSVP workflows. External calendar sync (future).
