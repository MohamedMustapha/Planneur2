import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { ANTI_FORGERY_HEADER } from '../session/csrf.interceptor';

/** Mirrors `Cracra.Modules.Kudos.Contracts`. */

export type KudoMode = 'counter' | 'points' | 'points-badges-leaderboard';

export type KudoPeriod = 'month' | 'year';

export type KudoScope = 'me' | 'unit' | 'department';

export type KudoDirection = 'received' | 'given' | 'all';

export interface BadgeView {
  readonly code: string;
  readonly labelKey: string;
  readonly earnedAt: string;
}

export interface KudoView {
  readonly id: string;
  readonly fromPersonId: string;
  readonly fromPersonName: string | null;
  readonly toPersonId: string;
  readonly toPersonName: string | null;
  readonly unitId: string;
  readonly departmentId: string;
  readonly category: string;
  readonly categoryLabelKey: string;
  readonly message: string;
  /** Zero whenever the department's mode does not show points. */
  readonly points: number;
  readonly createdAt: string;
}

export interface KudoCategoryOption {
  readonly code: string;
  readonly labelKey: string;
  readonly points: number;
}

export interface KudoRulesView {
  readonly departmentId: string;
  readonly mode: KudoMode;
  readonly showsPoints: boolean;
  readonly showsLeaderboard: boolean;
  readonly monthlyCapPerGiver: number;
  readonly givenThisMonth: number;
  readonly remainingThisMonth: number;
  readonly categories: readonly KudoCategoryOption[];
}

export interface EligiblePeer {
  readonly personId: string;
  readonly displayName: string;
  readonly unitId: string | null;
  /** unit | project | scope — why they may be recognised, which the picker groups by. */
  readonly relation: string;
}

export interface KudoPersonTotal {
  readonly personId: string;
  readonly personName: string | null;
  readonly count: number;
  readonly points: number;
  readonly badges: readonly BadgeView[];
}

export interface KudosSummary {
  readonly scope: string;
  readonly scopeId: string | null;
  readonly mode: KudoMode;
  readonly showsPoints: boolean;
  readonly from: string;
  readonly to: string;
  readonly total: number;
  readonly myReceived: number;
  readonly myGiven: number;
  readonly perPerson: readonly KudoPersonTotal[];
}

export interface LeaderboardRow {
  readonly rank: number;
  readonly personId: string;
  readonly personName: string | null;
  readonly count: number;
  readonly points: number;
  readonly badges: readonly BadgeView[];
}

export interface LeaderboardView {
  readonly scope: string;
  readonly scopeId: string | null;
  readonly mode: KudoMode;
  readonly from: string;
  readonly to: string;
  readonly rows: readonly LeaderboardRow[];
}

export interface AnnualCategoryGroup {
  readonly category: string;
  readonly labelKey: string;
  readonly count: number;
  readonly points: number;
  readonly kudos: readonly KudoView[];
}

export interface AnnualKudosView {
  readonly personId: string;
  readonly personName: string | null;
  readonly year: number;
  readonly mode: KudoMode;
  readonly showsPoints: boolean;
  readonly total: number;
  readonly points: number;
  readonly byCategory: readonly AnnualCategoryGroup[];
  readonly badges: readonly BadgeView[];
}

export interface GiveKudoResult {
  readonly id: string;
  readonly category: string;
  readonly points: number;
  readonly showsPoints: boolean;
  readonly remainingThisMonth: number;
  readonly earnedBadgeCodes: readonly string[];
}

/**
 * Recognition, as signals.
 *
 * The wall and the monthly counter are resources: both are things a screen watches. The leaderboard is not — it is
 * fetched on demand and can legitimately be refused, and binding a 403 into a resource would leave the wall
 * showing an error for a feature the department deliberately declined.
 */
@Injectable({ providedIn: 'root' })
export class KudosStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  readonly scope = signal<KudoScope>('unit');
  readonly direction = signal<KudoDirection>('received');
  readonly period = signal<KudoPeriod>('month');

  /**
   * The unit or department the widget is about.
   *
   * Null means "mine", which the server resolves. Not defaulted client-side: which unit somebody belongs to is a
   * server fact, and guessing it here would put a second copy of the org chart in the browser.
   */
  readonly scopeId = signal<string | null>(null);

  private readonly wallResource = httpResource<readonly KudoView[]>(() =>
    this.session.isAuthenticated()
      ? `/api/kudos?scope=${this.scope()}&direction=${this.direction()}&period=${this.period()}`
      : undefined,
  );

  private readonly summaryResource = httpResource<KudosSummary>(() =>
    this.session.isAuthenticated() ? `/api/kudos/summary?${this.scopeQuery()}` : undefined,
  );

  private readonly rulesResource = httpResource<KudoRulesView>(() =>
    this.session.isAuthenticated() ? '/api/kudos/rules' : undefined,
  );

  private readonly peersResource = httpResource<readonly EligiblePeer[]>(() =>
    this.session.isAuthenticated() ? '/api/kudos/eligible' : undefined,
  );

  readonly wall = computed<readonly KudoView[]>(() => this.wallResource.value() ?? []);
  readonly summary = computed(() => this.summaryResource.value());
  readonly rules = computed(() => this.rulesResource.value());
  readonly peers = computed<readonly EligiblePeer[]>(() => this.peersResource.value() ?? []);

  readonly isLoading = computed(
    () => this.wallResource.isLoading() || this.summaryResource.isLoading(),
  );

  readonly error = computed(() => this.wallResource.error() ?? this.summaryResource.error());

  /** The department's own answer, defaulting to a bare counter until the rules arrive. */
  readonly showsPoints = computed(() => this.rules()?.showsPoints ?? false);
  readonly showsLeaderboard = computed(() => this.rules()?.showsLeaderboard ?? false);
  readonly remaining = computed(() => this.rules()?.remainingThisMonth ?? 0);

  setScope(scope: KudoScope, scopeId: string | null = null): void {
    this.scope.set(scope);
    this.scopeId.set(scopeId);
  }

  setPeriod(period: KudoPeriod): void {
    this.period.set(period);
  }

  setDirection(direction: KudoDirection): void {
    this.direction.set(direction);
  }

  /**
   * The rules for one prospective recipient.
   *
   * Their department decides the categories and the price, so the modal asks about them rather than about the
   * giver — the two are usually the same department and occasionally, across a shared project, are not.
   */
  async rulesFor(personId: string): Promise<KudoRulesView> {
    return firstValueFrom(this.http.get<KudoRulesView>(`/api/kudos/rules?personId=${personId}`));
  }

  async give(toPersonId: string, category: string, message: string): Promise<GiveKudoResult> {
    const result = await firstValueFrom(
      this.http.post<GiveKudoResult>('/api/kudos', { toPersonId, category, message }),
    );

    this.reload();

    return result;
  }

  /**
   * The ranked board, or null where the department declined one.
   *
   * A 403 is an answer rather than a failure, so it is translated into "there is no leaderboard" instead of being
   * surfaced as an error. Every other status still throws.
   */
  async leaderboard(): Promise<LeaderboardView | null> {
    // fetch rather than HttpClient, because a 403 here is an answer and HttpClient's error channel would make it
    // indistinguishable from a failure — which means the anti-forgery header the interceptor would have added has
    // to be set here by hand, exactly as the reporting store does for its stream.
    const response = await fetch(`/api/kudos/leaderboard?${this.scopeQuery()}`, {
      headers: { [ANTI_FORGERY_HEADER]: '1' },
    });

    if (response.status === 403) {
      return null;
    }

    if (!response.ok) {
      throw new Error(String(response.status));
    }

    return (await response.json()) as LeaderboardView;
  }

  async annual(year: number): Promise<AnnualKudosView> {
    return firstValueFrom(this.http.get<AnnualKudosView>(`/api/kudos/me/annual?year=${year}`));
  }

  reload(): void {
    this.wallResource.reload();
    this.summaryResource.reload();
    this.rulesResource.reload();
  }

  private scopeQuery(): string {
    const scope = counterScopeFor(this.scope());
    const id = this.scopeId();

    return id
      ? `scope=${scope}&scopeId=${id}&period=${this.period()}`
      : `scope=${scope}&period=${this.period()}`;
  }
}

/**
 * The scope a counter can be drawn over.
 *
 * The wall's "me" has no counterpart: a count of recognition is about a group, and a group of one is a number
 * somebody already knows. It falls back to the unit, which is the group "my kudos" sit inside.
 */
export function counterScopeFor(scope: KudoScope): 'unit' | 'department' {
  return scope === 'department' ? 'department' : 'unit';
}

/**
 * Initials for an avatar.
 *
 * Two letters at most, and a question mark for somebody row-level security hid — their kudo is still on the wall
 * because one of the two parties is the reader, and a blank circle would read as a rendering fault rather than as
 * a name this reader is not entitled to.
 */
export function initialsOf(name: string | null): string {
  return (name ?? '?')
    .split(' ')
    .filter((part) => part.length > 0)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');
}
