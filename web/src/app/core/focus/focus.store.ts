import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { AccessStore } from '../access/access.store';
import { CONTEXTUAL_ROLES } from '../navigation/navigation';

/** Cache of the server-held preference, so a cold load renders the right shell before `/me` answers. */
const STORAGE_KEY = 'cracra.focus';

/**
 * Where each role's primary intent lives — v2 §02.1, mapped onto the routes this build actually has.
 *
 * The v2 nav table is written against the generic node tree from `01-org-model.md`, which is a later slice. Until
 * it lands, the same idea is expressed against the roles the access module already returns: the landing screen is
 * whichever one the person's job is *about*, and Focus mode shows that screen and nothing else.
 *
 * Order matters — the first match wins, so somebody who is both a member and a PMO focuses on the portfolio
 * rather than on their own week. The widest responsibility is the one the tool should open on.
 */
const FOCUS_ROUTE_BY_ROLE: readonly (readonly [string, string])[] = [
  [CONTEXTUAL_ROLES.pmo, '/portfolio'],
  [CONTEXTUAL_ROLES.departmentHead, '/department'],
  [CONTEXTUAL_ROLES.unitHead, '/team'],
  [CONTEXTUAL_ROLES.productOwner, '/projects'],
  [CONTEXTUAL_ROLES.projectLead, '/projects'],
  [CONTEXTUAL_ROLES.member, '/board'],
];

/**
 * Where Focus mode lands this viewer.
 *
 * Exported and pure so the rule can be pinned in a unit test without a TestBed: which screen a role's day is
 * *about* is a product decision, and it is the kind of decision that quietly rots when the roles change.
 */
export function focusRouteFor(roles: readonly string[]): string {
  const match = FOCUS_ROUTE_BY_ROLE.find(([role]) => roles.includes(role));

  return match ? match[1] : '/board';
}

/**
 * On for members, off for heads, PO and PMO (§02.2).
 *
 * A member's day is one task — fill the week — and everything else on their screen is noise. A head's day is
 * comparison, which needs the panels beside each other. The default follows the job, and either can override it.
 */
export function focusDefaultFor(roles: readonly string[]): boolean {
  const wide: readonly string[] = [
    CONTEXTUAL_ROLES.unitHead,
    CONTEXTUAL_ROLES.departmentHead,
    CONTEXTUAL_ROLES.pmo,
    CONTEXTUAL_ROLES.productOwner,
    CONTEXTUAL_ROLES.projectLead,
  ];

  return !roles.some((role) => wide.includes(role));
}

/**
 * Whether this URL is a deep link away from the focus target — the case §02.2 says renders in full mode for that
 * navigation only.
 *
 * Compares the path and drops the query, so `/board?week=3` is still the board. A prefix match rather than
 * equality, so a detail route under the focus target (`/projects/42` under `/projects`) counts as being there.
 */
export function isPiercedBy(url: string, focusRoute: string): boolean {
  return !url.split('?')[0].startsWith(focusRoute);
}

/**
 * Focus mode — v2 §02.2.
 *
 * A global toggle that collapses the shell to just the viewer's primary intent: the rail goes to icons, the top
 * bar loses search, notifications and the language switcher, and each page reduces to one panel and one action.
 * The point is to minimise time spent in the tool, which is why this is a first-class piece of shell state rather
 * than a per-screen preference.
 *
 * Three properties the spec is emphatic about, and which this class exists to guarantee:
 *
 *   * **It is presentation only.** Nothing here reaches an API, suppresses a validation error, or hides a
 *     destructive-action confirmation. If it ever needs to, the requirement has been misread.
 *   * **It is never a trap.** {@link active} can be false while {@link enabled} is true — that is a deep link
 *     rendering in full mode for one navigation — but the toggle is drawn in both states, always.
 *   * **It is a property of the person, not of the browser.** The value is written through the preferences
 *     endpoint, exactly like the theme; `localStorage` here is a cache so the first paint is right, never the
 *     record of intent.
 */
@Injectable({ providedIn: 'root' })
export class FocusStore {
  private readonly access = inject(AccessStore);
  private readonly router = inject(Router);

  /**
   * What this person chose, or null if they never have.
   *
   * Null is load-bearing: it is what lets {@link roleDefault} apply. A boolean initialised to `false` would opt
   * every member out of the mode the spec wants them in, and there would be no way to tell that from a member who
   * had genuinely turned it off.
   */
  private readonly preference = signal<boolean | null>(readCachedPreference());

  private readonly roleDefault = computed(() => focusDefaultFor(this.access.roles()));

  /** Whether the person is *in* Focus mode. What the toggle reflects. */
  readonly enabled = computed(() => this.preference() ?? this.roleDefault());

  /** The route Focus mode focuses on, for this viewer. */
  readonly focusRoute = computed(() => focusRouteFor(this.access.roles()));

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /**
   * True while a deep link is being honoured — Focus mode is on, but the viewer navigated somewhere that is not
   * their focus target, so this navigation renders in full.
   *
   * Derived from the URL rather than set by a guard, so it cannot get stuck: going back to the focus route makes
   * it false again by arithmetic, with nothing to remember to reset.
   */
  readonly pierced = computed(() => this.enabled() && isPiercedBy(this.url(), this.focusRoute()));

  /** What the shell and every page should actually key off. */
  readonly active = computed(() => this.enabled() && !this.pierced());

  constructor() {
    // `data-focus` beside `data-theme`, and for the same reason: the density contract in `_tokens.scss` is a
    // token override, and a token override needs a hook on the document element.
    effect(() => {
      document.documentElement.setAttribute('data-focus', this.active() ? 'on' : 'off');
      writeCachedPreference(this.preference());
    });
  }

  /** Applies a choice locally. Persisting it is {@link PreferencesStore.set}'s job, as with theme and language. */
  choose(enabled: boolean): void {
    this.preference.set(enabled);
  }

  toggle(): void {
    this.choose(!this.enabled());
  }

  /** Back to the focus screen, for the "Retour au mode focus" chip a pierced navigation shows. */
  returnToFocus(): void {
    void this.router.navigateByUrl(this.focusRoute());
  }

  /**
   * Adopts what the server holds for this person.
   *
   * Unconditional, unlike {@link ThemeStore.adoptProfileTheme}: theme is a property of the screen you happen to be
   * sitting at and may reasonably differ per machine, whereas Focus mode is a statement about how you work. The
   * server's answer is the answer, and a null one leaves the role default in charge.
   */
  adoptProfileFocusMode(enabled: boolean | null): void {
    this.preference.set(enabled);
  }

  /** What goes in the preferences PUT. Null means "never chosen" and must survive the round trip as null. */
  get chosen(): boolean | null {
    return this.preference();
  }
}

function readCachedPreference(): boolean | null {
  const cached = localStorage.getItem(STORAGE_KEY);

  return cached === 'true' ? true : cached === 'false' ? false : null;
}

function writeCachedPreference(value: boolean | null): void {
  if (value === null) {
    localStorage.removeItem(STORAGE_KEY);

    return;
  }

  localStorage.setItem(STORAGE_KEY, String(value));
}
