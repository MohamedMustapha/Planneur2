# v2 · 02 — Navigation, Focus Mode & Theme

**Amends:** the S6 shell and every Angular feature. This is the file that fixes "daunting 2010 management product."

## 1. Principle: navigation follows intent

Each role has one **primary intent** (`01 §2`). The app **lands the user on that intent** and shows only the nav they need. Everything else is either hidden or one click away under a "More" affordance — never in their face.

### Role → landing + visible navigation

Roles are depth-agnostic (`01 §2`): there is one **node-head** role, and what it sees follows the tree. Navigation therefore keys off *role + whether the node has children*, never off a level name.

| Role | Lands on | Primary nav (always visible) | Secondary ("More") | Hidden |
|---|---|---|---|---|
| **member** | My week | My week · Problems · Kudos | My node (read) | Portfolio governance, Budget, Strategy, Admin, Reports |
| **node-head, leaf node** (heads people directly — the "team lead" case) | My node board (people × week) | My week · My node · Items (node) · Reports · Problems | Kudos, Meetings | Budget*, Admin, Portfolio governance |
| **node-head, intermediate node** (heads child nodes) | My node board (children aggregated) | My node · Items · Budget · Problems · Meetings/CR · Reports · Strategy · Admin (node) | Kudos, Catalog | Admin (global), sibling branches |
| **node-head, top node** | My node overview (children tiles) | My node · Meetings/CR · Strategy · Reports · Budget (consolidated) | Portfolio, Problems | Admin (global) |
| **PO** | My item | My items · Team progress · Meetings/COPIL · Problems (item) | Reports (item), Catalog | Node admin, Budget, Strategy edit |
| **PMO** | Portfolio | Portfolio · Strategy · Meetings · Reports | Problems pipeline, Budget (read) | Admin (global), single-person boards |
| **admin** | Admin | Admin (global or node) · everything operational | — | — |

\* Budget visibility is a **profile capability** (`10 §3`), not a level: a leaf-node head whose profile grants `budget:true` sees it.

The three head rows are **one role rendered differently by tree position** — the app asks "does my node have children?", never "am I a bureau head?". Adding a 5th level changes nothing here.

Nav is computed server-side from `whoami` (S2/S14) and delivered to the shell; the client renders only permitted items. Deep links to a hidden page still enforce RLS — nav hiding is UX, not security.

## 2. Focus mode

A global toggle (top bar, persisted per user) that collapses the shell to **just the primary intent**, removing everything not needed to complete it. Goal: minimize time on the tool.

### What Focus mode does everywhere
- Collapses the left nav to icons; hides the top-bar search, notifications, shortcuts hints, and secondary period tabs.
- Hides right-rail widgets (À venir, Raccourcis, keyboard hints, Mon unité placeholder — all seen in the current *Mon tableau*).
- Reduces each page to **one primary panel + one primary action**. Secondary panels become a single "Show more" link.
- Shows a one-line **guidance banner**: what to do here, and the single next step.

### Per-role Focus target
| Role / position | Focus screen = only… |
|---|---|
| member | the weekly grid + 35h meter + Quick-add (+ import, if the profile allows). Nothing else. |
| node-head over people | the node board (people × week) + "Generate brief". |
| node-head over child nodes | the node board (children aggregated) + the top obligation ("N problems await triage" / "N licences expire"). |
| PO | the item task-progress timeline + "Open COPIL". |
| PMO | the portfolio catalog + "Review objectives". |

### Persistence & escape hatches (was under-specified)
- Stored as `Person.focus_mode_enabled` (server-side, not localStorage) so it follows the user across devices; default seeded per role by `it-global-admin` (`08` feature flags). Default **on** for member, **off** for all heads/PO/PMO.
- The toggle is **always visible** in the top bar, in both modes — Focus mode must never be a trap.
- **Focus mode never hides an item the user has an open obligation on.** If they have an overdue action item (`07`), a problem awaiting their triage (`05`), or an over-35h week, a single dismissible **obligation chip** appears in the Focus banner. Obligations are the one thing allowed to pierce Focus.
- Deep links always work: opening a hidden page in Focus mode renders it in full mode for that navigation only, with a "Retour au mode focus" chip.
- Focus mode is **presentation only** — it never changes what an API returns, never suppresses a validation error, and never hides a destructive-action confirmation.

## 3. Declutter rules (apply to existing screens)

Concrete fixes tied to the current build:

- **Mon tableau (activity board):** remove the right rail in normal mode too — fold "À venir" into a slim strip above the grid, drop "Raccourcis"/keyboard-hint block entirely (surface shortcuts in a `?` popover), remove the "Mon unité" placeholder. Keep: 35h meter, Import dropdown, Quick-add, Semaine/Mois/Liste (Liste only in full mode).
- **Reports / Synthèse:** (rollup shape now generic — `01 §4`) the raw per-unit table (EFFECTIF / RÉALISÉ 97,5 / QOL) is **not meeting-ready**. Split into two products: (a) a **consultation view** for a lone unit/bureau-head (current table, fine), and (b) a **brief view** built for exposing to n+1 and for slotting into the parent node's report as one block — see `07`/S8 delta: whole hours rounded, headline sentence per unit, top 3 items, risks, no decimals. Default the Reports page to the brief.
- **Portfolio:** stop showing four empty status columns as the primary view; the primary view is the **catalog of identity cards** (`03`). The kanban becomes a secondary "Flux" tab.
- **Projects list:** add a **"New project" primary button** and category facets; keep the list dense but add the identity-card drawer on row click.
- **Kudos:** keep as its own page; **remove kudos widgets from Team/board headers** (you said it needn't report elsewhere) — it only lives on the Kudos page and the monthly team footer.
- **Finance:** never render an empty page awaiting a project selection — land on the **consolidated view** and drill down (`04`).

## 4. Theme tokens (normative — the design pack renders these, it does not invent them)

Ship as CSS custom properties in `shared/ui/theme.css`. Adjectives were not actionable; these are.

### Spacing & size
| Token | Value | Use |
|---|---|---|
| `--space-1…6` | 4 · 8 · 12 · 16 · 24 · 32 px | 8px-based, `space-1` only for icon gaps |
| `--control-h` | **44px** (`--control-h-sm` 36px) | buttons, inputs, selects. 36px allowed **only** inside dense grids |
| `--row-h` | **56px** (Focus mode: 48px) | primary list/board rows |
| `--radius` | 10px (`--radius-sm` 6px, `--radius-pill` 999px) | cards / inputs / pills |
| `--page-max` | 1440px, content gutter `--space-6` | |

### Type
| Token | Value |
|---|---|
| `--font-base` | **16px** / 1.5 (v1 was ~13–14px — this is the single biggest anti-2010 lever) |
| `--font-sm` / `--font-xs` | 14px / 12px (`xs` for labels only, never body) |
| `--font-h1/h2/h3` | 28 / 22 / 18px, weight 600 |
| `--font-num` | `font-variant-numeric: tabular-nums` — **mandatory** on hours, money, counts |

### Color (light; dark = same roles, inverted ramp)
| Token | Value | Use |
|---|---|---|
| `--primary` / `--primary-hover` | `#2E5BFF` / `#1E45D8` | primary actions |
| `--primary-soft` | `#EAF0FF` | selected rows, active nav |
| `--surface` / `--surface-raised` | `#FFFFFF` / `#F7F9FC` | page / cards |
| `--border` | `#E3E8F0` — **used sparingly**; prefer `--shadow-1` | |
| `--shadow-1` | `0 1px 3px rgba(16,24,40,.08)` | cards instead of hairlines |
| `--text` / `--text-muted` | `#101828` / `#5B6B84` | |

**Semantic — lifecycle** (`03`): `--state-considered #64748B` · `--state-committed #2E5BFF` · `--state-active #12B76A` · `--state-vnext #7C4DFF` · `--state-dephase #B4841F`.
**Semantic — activity** (`05`/`10`): `--act-build #4B5BD7` · `--act-run #0E9F9F` · `--act-qol #12B76A` · `--act-admin #7C4DFF`.
**Semantic — status:** `--warn #F79009` · `--danger #D92D20` · `--ok #12B76A`.

Pills use the semantic color at 12% opacity as background with the full color as text — never a saturated fill behind white text at small sizes (that's the grim-corporate look).

### Density contract
Full mode uses `--row-h`/`--control-h`. **Focus mode tightens vertical rhythm but never shrinks controls below 40px** — Focus is about *fewer things*, not *smaller things*. Getting this backwards reproduces exactly the problem we're fixing.

## 5. Guidance system (make "what do I do here" answerable)

Three mandatory, structured elements — not ad-hoc copy:

1. **Page purpose** — one sentence under every page title, stating what this page is *for* and for whom. i18n key `<feature>.page.purpose`.
2. **Next-best-action** — each landing page computes **one** suggested action from real state, shown as the primary button plus a short reason. Examples: member with an unfilled week → *"Il vous reste 12 h à saisir cette semaine"*; node-head with 3 untriaged problems → *"3 problèmes attendent votre tri"*; PO with an iteration ending in 2 days → *"L'itération se termine jeudi — clôturez ou prolongez"*. Served by `GET /api/guidance/next-action` so it's testable, not template logic.
3. **Empty states** — every list/board defines: one illustration-free line of explanation + exactly one primary CTA + (optional) one "learn more" link. No empty grids, ever. (`GET /api/portfolio/catalog` returning zero rows renders *"Aucun projet dans votre périmètre — créez-en un ou cherchez s'il existe déjà ailleurs"* with both actions.)

**First-run:** a 3-step coach-mark tour per role (not a modal wall), dismissible forever, replayable from the user menu.

## Tests (delta)
- **Unit:** nav computed correctly per role; hidden items absent; next-best-action selection rules per role/state.
- **Integration:** a member deep-linking to `/finance` gets 403 (RLS) even though nav hid it; `focus_mode_enabled` persists server-side; obligation chip appears for an overdue action.
- **Accessibility:** contrast ≥ 4.5:1 for all text tokens on their surfaces; every control ≥ 40px; full keyboard path through the week grid.
- **E2E:** toggle Focus mode as each role → only the focus panel + primary action remain; deep-link into a hidden page renders full mode with a return chip; every landing page renders a non-empty next-best-action.
