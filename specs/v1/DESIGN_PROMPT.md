# Claude Design — Prompt Pack

Copy-paste prompts for **Claude Design** (canvas + iterate-via-chat). Start with **Prompt 0 (design system)** so every later screen inherits the same tokens, then run the screen prompts. Each is self-contained; paste one, iterate, then move on. All screens are **Angular 22 + Mobiscroll timelines**, trilingual (fr/en/es) — design with French as the longest-string reference so nothing clips.

> Global note to include in every screen: *"This is an internal enterprise tool used daily by IT and other business units. Prioritize information density, scannability and speed over marketing polish. Desktop-first (1440px), responsive down to tablet. Neutral, calm, professional. Assume a persistent left nav (64px collapsed / 240px expanded) and a top bar with department switcher, language switcher (FR/EN/ES), and user menu."*

---

## Prompt 0 — Design system & shell

> Design a cohesive **design system and app shell** for an internal enterprise "Department Activity & Portfolio" platform (IT-first, but department-agnostic). Deliverables on the canvas: a **style tile** + the **empty app shell**.
>
> **Style tile:** a restrained enterprise palette — one primary (calm blue/indigo), one neutral gray ramp (8 steps), and semantic colors for status: *considered* (slate), *committed* (blue), *active* (green), *déphasé/archived* (muted amber-gray), plus warning (amber) and danger (red). Activity-type accents: BUILD (indigo), RUN (teal), Quality-of-life (green), Recruitment/Admin (violet). Typography: one clean sans (Inter or similar), clear type scale (12/14/16/20/28), tabular numerals for hours/costs. 8px spacing grid. Rounded-6 cards, subtle 1px borders, minimal shadow. Show chips, badges, buttons (primary/secondary/ghost/danger), inputs, tabs, a data-table row, a status pill set, and a kudo badge.
>
> **App shell:** left nav with sections *My board, Team, Unit, Department, Projects, Portfolio, Reports, Kudos, Finance, Settings*; collapsed (icons) and expanded (icon+label) states. Top bar: department switcher, week/period picker, FR/EN/ES language switcher, search, notifications, avatar menu. Content area with a page-title row + a role/scope selector. Show the shell in light mode (dark mode as a variant).
>
> Design for information density; this is used all day. Provide the tokens as reusable styles.

---

## Prompt 1 — Individual activity board (log my activity)

> Design the **"My board / log my activity"** screen — the primary daily interaction for any employee (dev, ops, accountant, HR…).
>
> Layout: a **horizontal rotating timeline** (Mobiscroll-style) across the current week, with **rows = my activity lanes** (BUILD, RUN, Quality-of-life, Recruitment/Admin). I create/drag time slots onto a lane to log work. Two input paths, both visible:
> 1. **Manual slot** — drag on the timeline; a slot editor opens (activity type from my department's taxonomy, project, hours, note). Actuals may differ from a planned slot — show planned as a lighter ghost block behind a solid actual block.
> 2. **Pull-from-source dropdown** — "Add from Azure DevOps / ServiceNow": a dropdown listing *my current-sprint tasks* / *tickets assigned to me*; selecting one pre-fills type + project + external reference, I just set hours.
>
> Include a **35h/week meter** for the week (fills as I log; soft-warn amber past 35h, or hard-block styling if my department enforces it). Show my **team/unit context** in a side rail (my unit's members, so I know who I'm working alongside — and a quick "give kudo" affordance on a teammate). Left: mini week nav. Keep it fast to fill; a keyboard-friendly quick-add.
>
> Trilingual labels; French reference for width.

---

## Prompt 2 — Team board (lead view + auto status report)

> Design the **Team board** for a team/unit lead. A **rotating timeline** with **rows = my unit's members**, cells = their logged activities color-coded by type (BUILD/RUN/QoL/Admin). Above it: a summary strip — total hours by type, coverage, members over/under 35h, kudos this month.
>
> Add a prominent **"Generate status report"** control with period toggles (Weekly / Monthly / Custom). When clicked, a **report panel** slides in: deterministic tables/charts render instantly (hours by member, by project, by type; iteration progress; upcoming copil/deadlines), and an **AI-written narrative summary streams in below**, clearly labeled "AI summary — on-prem model," with a Regenerate and an Export-PDF button. Show the streaming state.
>
> The board must feel like mission control for a lead: dense, scannable, one-glance health. Trilingual.

---

## Prompt 3 — Project view (team grouped by department)

> Design the **Project detail / project board** for a project lead or PO. Header: project name, BUILD/RUN classification chip, lead department, manual cost, current iteration, portfolio state pill (Considered/Committed/Active/Déphasé).
>
> Main area: a **BUILD-style task-progress timeline** (Mobiscroll) where **each row is a project member grouped by their department, then function** — e.g. a *Dev* group (dev, tech-lead, architecte), a *Design* group, an *Ops* group — with a collapsible group header per department. Task events sit on member rows and carry a **progress bar** on the event (percentage from logged actuals / linked DevOps state). Iteration boundaries drawn as shaded ranges with a 1w/2w/1m/custom length selector when creating one.
>
> Right rail: team composition (headcount by department/function), cost, and cross-department contribution split. This screen must make "who from which department is doing what, and how far along" obvious at a glance. Trilingual; French width reference.

---

## Prompt 4 — Department view (units × projects, with special days)

> Design the **Department board** for a department head. A large **rotating timeline** where **each row is a unit of the department, and within it each project appears on its own line**, the grid filled with the units' activities (color-coded by type). Collapsible unit groups.
>
> Overlay **special days and deadlines as marked vertical columns / badges** on the timeline — e.g. *Patch party*, *Audit*, *Go-live*, *Freeze* — styled by severity, with a "coming up" strip at the top listing the next events. Also overlay recurring **meetings** (weekly, copil) as thin bands.
>
> Top summary: per-unit load, projects by portfolio state, cross-department shared projects flagged (knowledge-flow read access), and a compact cost snapshot. This is the head's daily situational view — dense, calm, everything visible without drilling. Trilingual.

---

## Prompt 5 — Portfolio board

> Design the **Portfolio board** (PMO / heads). Lanes by lifecycle state: **Considered · Committed · Active · Déphasé (archived)**. Each project is a card showing priority, lead department, BUILD/RUN classification, manual cost, and current iteration. Cards are draggable between lanes; illegal moves are visibly blocked (guarded transitions).
>
> For Active items, show a compact **iteration strip** on the card (sequence of iterations with a 1w/2w/1m/custom length selector) and a phase indicator. Filters: my projects / my unit / my department / all (bounded by role). A right drawer for a selected project: decision notes, transition history (audit), team & cost. Déphasé lane visibly read-only/muted. Trilingual.

---

## Prompt 6 — RUN work-order assignment (Mobiscroll 6a) & shift scheduler (6b)

> Design **two RUN scheduling screens** sharing the timeline canvas.
>
> **6a — Work-order assignment** (helpdesk-style): a Mobiscroll timeline with **rows = team members** and a **fixed top row = pool of unassigned work orders** (tickets pulled read-only from ServiceNow). Drag a work order from the pool onto a member's row to assign (creates a planned RUN activity); drag back to unassign. Show ticket meta on the block (priority, source, ref). A filter for the queue and an unassigned-count badge.
>
> **6b — Shift scheduler**: rows = members, columns = time; cells = shifts (morning / afternoon / on-call) from shift templates. Coverage warnings when a slot is understaffed; conflict/over-35h styling on double-bookings. A shift palette to drag from, and a weekly coverage summary bar.
>
> Both trilingual, dense, drag-first. Present them as tabs of one "RUN scheduling" page.

---

## Prompt 7 — Kudos wall & leaderboard

> Design the **Kudos** experience. Default **counter mode**: a clean per-person recognition count and a **monthly totals widget that lives on the team board**. A **"Give kudo"** modal: pick a teammate (from my unit / project), a category (Initiative, Cleanup, Improvement, Mentoring, Above-and-beyond), a short message; show a monthly-cap indicator.
>
> Design the optional **points-badges-leaderboard mode** (a department can enable it): a leaderboard for the unit/department (period toggle), badge tiles a person has earned, and points on kudo cards. Keep it tasteful and work-appropriate — recognition, not gamified noise. Also design **"My annual kudos"**: all kudos received in a year grouped by category with messages, exportable for review claims. Trilingual.

---

## Prompt 8 — Contextual status report page

> Design the standalone **Status report** page that **auto-adapts to the viewer's role**. A scope selector reflecting the viewer's rights (My work / My project team / My unit / My department / My project / Portfolio) with the widest allowed scope pre-selected, and a period toggle (Week / Month / Custom) + language.
>
> Body: deterministic sections first (hours by type, 35h status, by-member/by-project tables, iteration burn, portfolio counts by state incl. déphasé this period, upcoming audits/patch-parties) as tight tables and small charts; then the **AI narrative summary** streaming in, labeled on-prem, with regenerate + export-to-PDF. Make it printable/exportable and equally readable for a dev viewing "my work" or a PMO viewing "portfolio." Trilingual, French width reference.

---

### Iteration tips (say these to Claude Design after a first pass)
- "Tighten density — this is used 8h/day; reduce padding, show more rows."
- "Make the status pills and activity-type accents exactly match the design-system tokens from Prompt 0."
- "Add the dark-mode variant."
- "Show the empty state and the loading/streaming state for the AI summary."
- "Verify French strings don't clip; adjust column widths."
