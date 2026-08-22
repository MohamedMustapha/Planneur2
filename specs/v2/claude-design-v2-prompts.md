# Claude Design — v2 Prompt Pack

**Replaces the v1 design pack.** New visual language: **bigger controls, brighter and calmer colors, leaner pages, guidance about what to do, and a Focus mode** that minimizes time on the tool — still professional, but away from the tiny grim 2010-corporate feel. Run **Prompt 0** first (theme + shell + Focus mode); every screen inherits it.

> Paste this global note into every screen prompt: *"Internal enterprise tool for a public administration (IT plus other services: bilateral, finance, general services). Trilingual FR/EN/ES — design with French as the longest-string reference. Desktop-first 1440px, responsive to tablet. Bigger controls (min 44px targets), brighter and calmer palette, generous white space, strong hierarchy, one-line purpose statement per page, and a single clear primary action. Every page has a Focus-mode variant that strips it to the primary task. Show empty states with one helpful sentence + one primary CTA."*

---

## Prompt 0 — Theme, shell & Focus mode

> Design the **design system, app shell, and Focus mode** for "Activité & Portefeuille," an internal platform for a public administration.
>
> **Move away from dense 2010-corporate.** Use these **exact tokens** (from `v2/02 §4` — render them, don't invent alternatives):
> - Base font **16px**/1.5; h1/h2/h3 = 28/22/18 semibold; tabular numerals on all hours, money and counts.
> - Controls **44px** high (36px only inside dense grids); primary rows **56px**; radius 10px; 8px spacing scale.
> - Primary `#2E5BFF` (hover `#1E45D8`, soft `#EAF0FF`); surface `#FFFFFF` / raised `#F7F9FC`; text `#101828` / muted `#5B6B84`; border `#E3E8F0` used **sparingly** — prefer the soft shadow `0 1px 3px rgba(16,24,40,.08)` over hairlines.
> - Lifecycle: Envisagé `#64748B` · Engagé `#2E5BFF` · Actif `#12B76A` · **En attente v2** `#7C4DFF` · Déphasé `#B4841F`. Activity: BUILD `#4B5BD7` · RUN `#0E9F9F` · Qualité-de-vie `#12B76A` · Recrutement/Admin `#7C4DFF`. Warn `#F79009` · Danger `#D92D20`.
> - **Pills:** semantic color at 12% opacity as background, full color as text. Never a saturated fill behind small white text — that's the grim-corporate look we're leaving.
>
> Deliver a **style tile** showing: buttons (primary/secondary/ghost/danger), inputs, selects, big status pills, the identity-card chip set, a progress ring, tabs, a data row, a guidance banner, an obligation chip, and an empty state.
>
> **App shell:** slim left nav that renders **only the items the current role needs** (role-driven), collapsible to icons; top bar with **Service/Bureau switcher**, period picker, FR/EN/ES switch, and a prominent **Focus-mode toggle**. Content area = a one-line page purpose + one primary action, then the primary panel.
>
> **Focus mode:** design the toggle and the transformation — nav collapses to icons, top-bar search/notifications/shortcut-hints disappear, right rails and secondary tabs vanish, and the page reduces to **one primary panel + one primary action + a one-line guidance banner**. Show the same page (e.g. the weekly activity grid) in full vs Focus side by side. Light and dark variants.

---

## Prompt 1 — Member: "My week" (lean activity entry)

> Redesign the daily **activity entry** screen for a normal contributor, much leaner than before. A **rotating weekly timeline** (Mobiscroll-style), rows = my activity lanes (BUILD · RUN · Qualité-de-vie · Recrutement/Admin). I drag a slot to log; planned shows as a ghost block behind a solid actual.
>
> Keep only: a **big 35h-week meter**, a **Quick-add** primary button, and an **"Importer depuis Azure DevOps / ServiceNow"** dropdown (pick a sprint task / assigned ticket → pre-fills type + project + reference). **Remove** the old right rail entirely (no "À venir", no "Raccourcis", no keyboard-hint block, no "Mon unité" placeholder). Add a slim one-line guidance banner ("Renseignez vos créneaux de la semaine — objectif 35 h") and a single secondary link "Signaler un problème."
>
> Show the **Focus-mode variant**: just the grid + 35h meter + Quick-add. This is the default view for contributors. Big controls, calm, fast.

---

## Prompt 2 — Portfolio catalog + project identity card

> Design the **Portfolio** as a **catalog of project identity cards** (this is the landing, not an empty kanban). A facet bar filters by **type** (Projet · Plateforme · Produit · Service RUN · Initiative · Intelligence), **category**, **classification** (BUILD/RUN/Mixte), **lifecycle** (incl. *En attente v2*), owner bureau/service, and a **"Partagé / consommé par d'autres"** toggle. A prominent **"Rechercher un projet similaire"** search (so a bureau can check something exists before requesting a build) and a **"Nouveau"** primary button.
>
> **Identity card (grid tile):** code · name · type & category chips · classification · **lifecycle pill** · owner bureau · lead/PO · team headcount · budget headline · current iteration · dependency count · linked-objective badge. Bright, scannable, big.
>
> **Identity card (detail drawer):** full team grouped by **Bureau → Unit → function**, an **iterations timeline with epics/features** (with an *En attente v2* section listing queued epics), a **dependency view** (consumes / consommé par — e.g. "consomme k8s"), a **budget breakdown** (capex/opex, licenses, external workers), linked **strategy objectives**, the **originating problem** if any, and history. Also design the secondary **"Flux" tab**: lifecycle kanban with guarded drag.

---

## Prompt 3 — New project wizard

> Design a short, guided **"Nouveau projet / élément"** wizard (big controls, one decision per step, progress indicator): (1) **Type & catégorie**; (2) **Identité** (nom, code auto, bureau propriétaire, lead/PO); (3) **Origine** — link an existing **Problème** or **Objectif**, or "à partir de zéro"; (4) **Cadrage** — classification, first iteration length (1 sem / 2 sem / 1 mois / perso), optional first epics, **dépendances** (search catalog → "consomme k8s"); (5) **Estimation & budget** (optional). A "Candidat" fast-path uses only steps 1–2. End on the new identity card. Calm, confident, not a form wall.

---

## Prompt 4 — Finance: consolidated drill-down

> Design the **Finance** page so it **never starts empty** — it lands on a **consolidated roll-up** and drills down: **Service → Bureau → Élément → Composant de coût**. A breadcrumb + expandable tree/table with columns **Capex · Opex · Prévu · Écart**, a service-head landing at service level and a bureau-head at bureau level. Toggles: capex-only / opex-only / both, include effort cost (rate card) on/off, fiscal year, planned-vs-actual variance. Charts: capex vs opex per bureau/item, top cost drivers.
>
> Two dedicated tabs: **Licences** (table + renewal calendar; SharePoint, SaaS, IDEs; attachable to an item or the bureau) and **Prestataires externes** (consultants: vendor, rate, contract end; contract-end calendar; link to their planned activity). Everything rolls into the consolidated view. Big controls, bright, export-to-Excel. Head/PMO only.

---

## Prompt 5 — Problems / Irritants

> Design the **Problèmes / Irritants** experience. A big **"Signaler un problème"** primary action opens a guided form: title, **catégorie** (processus · projet · qualité-de-vie · outillage · données), **où ça fait mal** (personne/unité/bureau/service), **temps perdu** (h/sem) and **fréquence** — with **live duplicate suggestions** as you type (and a nudge if a matching product already exists in the catalog).
>
> Main view: a **ranked board** (by impact/votes) or columns by status (Nouveau · Trié · Accepté · Converti · Résolu). **Detail**: description, **propositions de solution** (anyone can add one), an upvote / "moi aussi", comments; for heads a **triage** control (accepter / doublon / refuser + motif) and, for IT/PMO, **"Convertir en projet"** that opens the wizard (Prompt 3) pre-filled and links the problem to the created item. Also an **IT incoming pipeline** view (accepted, not yet converted). Warm, encouraging, big controls — this should feel safe and quick to use.

---

## Prompt 6 — Strategy → Objectives

> Design the **Stratégie** overview for a service or bureau head. Objectives as **cards with progress rings**, status (on-track / at-risk / off-track / done), metric (number/percent/currency/milestone) with baseline→current→target, and due date. Each objective lists its **contributing projects** (from the catalog) with a mini-state, and can link a **problem** as a driver. **Objective detail**: metric trend, contributing items/problems, optional key results, history. An **"Alignement"** view surfaces gaps — objectives with no project, projects with no objective — each one-click linkable or convertible into a new project. Calm, executive, legible from across a room (for COPIL projection).

---

## Prompt 7 — Meetings & CR (minutes)

> Design **Réunions & Comptes-rendus**. A list of upcoming occurrences by **level** (Unité · Bureau · Inter-bureaux · Service/COPIL) with "Écrire le CR". The **CR editor** (guided, big controls): agenda, **présents/absents** (picked from the directory), **décisions**, **actions** — each action has an owner, a due date, and an **entity link** (Problème / Projet / Objectif) via a picker — and a short **synthèse** (with an optional AI first draft, labeled on-prem).
>
> Design the **action tracker** (open/overdue actions rolled onto owners' boards and the bureau/service overview) and a **"Derniers CR"** strip for dashboards. Then design the **meeting-ready Brief** that replaces the raw synthèse: **whole-hour rounded** figures (no decimals), **one headline sentence per unit/bureau**, top-3 items and risks, upcoming deadlines/COPIL — and make it **stack upward** (unit → bureau → service) so a head can compose subordinate briefs into one COPIL document. Keep the raw decimal table available under a "Détails" disclosure.

---

## Prompt 8 — Node overview (one screen, every level)

> Design **one supervisory overview that works at any depth of an org tree** — the deployment decides whether its levels are called Service/Bureau/Pôle, Direction/Département/Équipe, or anything else, so **never hardcode a level name in the design**; take the label from a `levelLabel` prop.
>
> The screen shows the current node's name and its configured level label, then **one tile per child node**, each with: child name, headcount **for its whole subtree**, current-week load, key items, budget variance, risk flag — and a drill-in affordance. When the node has no children, the same screen shows **people rows** instead of node tiles. Below: open actions, latest CR, problems awaiting triage, strategy rings.
>
> Design a **breadcrumb** showing the path from the root to here, so a head can climb and descend. Show the screen three times — at a top level (few big tiles), at a middle level, and at a leaf (people rows) — proving one layout serves all. Primary action adapts: "Écrire le CR" high in the tree, "Trier les problèmes" mid, "Générer la synthèse" at a leaf.

## Prompt 9 — Admin (global & bureau)

> Design **two administration surfaces**.
>
> **Admin global (IT):** sections Org (create/rename/**re-parent** Services/Bureaux/Units, LDAP sync status), Personnes, **RBAC** (grant/revoke roles at any scope, "source LDAP vs override" badges, expiry), Taxonomies par défaut, Intégrations (Azure DevOps/ServiceNow/LLM/stockage), Indicateurs & thème par défaut, **Journal d'audit**.
>
> **Admin bureau (chef de bureau):** **Membres** (the team-management UI moved out of the boards: add/move a person into the bureau/unit, set unit-head, set functional role), Config du bureau, Config budget (rate cards, règles capex/opex, licences, prestataires), RBAC du bureau, Stratégie. Guided, big-control forms; destructive actions confirmed; role source badges and inline audit. Professional, reassuring, not intimidating.

---

## Prompt 10 — Profile variants (prove the versatility)

> Take the "My week" screen (Prompt 1) and design **four bureau-kind variants**, same skeleton, different vocabulary and controls — proving the tool isn't IT-only:
> 1. **A relationship/diplomacy profile** — subtypes like *préparation de sommet, suivi de relation, accueil de délégation, rédaction de dossier*; picker is "Initiative / dossier"; **no** integration dropdown, **no** work-order pool, **no** shift scheduler. Upcoming *sommet* shown as a deadline marker.
> 2. **An advisory / analysis profile** — subtypes *recherche, analyse de sources, production de note, veille, briefing*; picker is "Livrable"; note-count visible in the week header.
> 3. **A casework profile (subsidies, licensing)** — subtypes *instruction de dossier, contrôle d'éligibilité, campagne de paiement, support demandeur, recours*; picker is "File / lot de dossiers"; a **stock restant** gauge next to the 35h meter; this profile DOES get the work-order pool and shift scheduler.
> 4. **An internal-support profile (HR, accounting)** — the simplest variant: *paie, campagne de recrutement, intégration, comptabilité*; no timeline archetypes beyond the plain week grid.
>
> The point: identical layout and tokens, zero IT jargon leaking into non-IT bureaux, and controls a bureau doesn't use are **absent, not disabled**.

## Prompt 11 — Guidance states (purpose, next-best-action, empty, first-run)

> Design the **guidance system** as reusable components, shown across three example pages:
> - **Page purpose** — one muted sentence under every page title.
> - **Next-best-action banner** — one computed suggestion with a reason and a single primary button. Show four real variants: *"Il vous reste 12 h à saisir cette semaine"* (member), *"3 problèmes attendent votre tri"* (bureau-head), *"L'itération se termine jeudi — clôturez ou prolongez"* (PO), *"2 licences arrivent à échéance sous 30 jours"* (bureau-head, finance).
> - **Obligation chip** — the one element allowed to pierce Focus mode (overdue action item, untriaged problem, week over 35 h). Design its Focus-mode and full-mode placement.
> - **Empty states** — one explanatory line + exactly one primary CTA, for: empty catalog, no problems, no CR yet, no objectives. Never a bare empty grid.
> - **First-run coach marks** — a 3-step, non-modal tour for a member and for a bureau-head; dismissible forever, replayable from the user menu.

## Prompt 12 — Catalog discovery & request-access

> Design the two-tier catalog visibility. A **discovery card** (for an item the viewer may see but not open) shows only name, code, type, category, owner bureau and lifecycle — visibly "lighter" than a full identity card, with a clear **"Demander l'accès / Contacter le bureau propriétaire"** primary action instead of a dead 403. Design the request modal (why you need access, sent to the owning bureau-head) and the owner's inbound-request handling.
>
> Also design the **"un projet similaire existe peut-être"** moment: when someone starts the new-project wizard (Prompt 3) or files a problem (Prompt 5), matching discovery cards surface inline with a *"C'est déjà ça ? Contactez le bureau X"* affordance. This is the mechanism that stops duplicate spend — make it feel helpful, not obstructive.

---

### Iteration tips (say after a first pass)
- "Make it lighter and bigger — this must not feel like 2010 corporate software; increase control size and spacing, brighten the accents."
- "Match all lifecycle/status pills and activity accents to the Prompt 0 tokens exactly."
- "Show the Focus-mode variant and the empty state with a single primary CTA."
- "Round hours to whole numbers in the Brief; keep decimals only under Détails."
- "Check French strings don't clip; widen columns and buttons accordingly."
