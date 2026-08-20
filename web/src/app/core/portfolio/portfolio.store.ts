import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { DepartmentScopeStore } from '../scope/department-scope.store';

/** Mirrors `Cracra.Modules.Portfolio.Contracts`. */

export type PortfolioState = 'considered' | 'committed' | 'active' | 'dephase';

export type IterationLength = 'oneweek' | 'twoweeks' | 'onemonth' | 'custom';

export interface IterationSummary {
  readonly id: string;
  readonly sequence: number;
  readonly name: string;
  readonly length: string;
  readonly startsOn: string;
  readonly endsOn: string;
  readonly state: string;
}

export interface PortfolioItemSummary {
  readonly id: string;
  readonly projectId: string | null;
  readonly name: string;
  readonly state: PortfolioState;
  readonly priority: number;
  readonly departmentId: string;
  readonly departmentNameKey: string;
  readonly decisionNotes: string | null;
  /** Null when the caller cannot read the project: the card shows without a cost rather than vanishing. */
  readonly costAmount: number | null;
  readonly costCurrency: string | null;
  readonly classification: string | null;
  readonly currentIteration: IterationSummary | null;
  readonly iterationCount: number;
}

export interface PortfolioLane {
  readonly state: PortfolioState;
  readonly items: readonly PortfolioItemSummary[];
}

export interface PortfolioBoard {
  readonly lanes: readonly PortfolioLane[];
}

export interface TransitionRecord {
  readonly fromState: string | null;
  readonly toState: string;
  readonly isReversal: boolean;
  readonly reason: string;
  readonly decidedBy: string;
  readonly decidedAt: string;
}

export interface PortfolioItemDetail {
  readonly item: PortfolioItemSummary;
  readonly iterations: readonly IterationSummary[];
  readonly history: readonly TransitionRecord[];
}

/**
 * The portfolio, as signals.
 *
 * The lanes come from the server already grouped, including the empty ones. Regrouping them here would mean the
 * client deciding what a lane is, and the two definitions drifting the first time a state is added.
 */
@Injectable({ providedIn: 'root' })
export class PortfolioStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);
  private readonly departments = inject(DepartmentScopeStore);

  /** "all" or "mine". Both narrow what RLS already allowed; neither can widen it. */
  readonly scope = signal<'all' | 'mine'>('all');

  private readonly boardResource = httpResource<PortfolioBoard>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const params = new URLSearchParams();
    const departmentId = this.departments.selected()?.id;

    if (departmentId) {
      params.set('departmentId', departmentId);
    }

    if (this.scope() === 'mine') {
      params.set('scope', 'mine');
    }

    const query = params.toString();

    return query ? `/api/portfolio?${query}` : '/api/portfolio';
  });

  readonly lanes = computed<readonly PortfolioLane[]>(() => this.boardResource.value()?.lanes ?? []);
  readonly isLoading = this.boardResource.isLoading;

  readonly total = computed(() =>
    this.lanes().reduce((count, lane) => count + lane.items.length, 0),
  );

  async get(itemId: string): Promise<PortfolioItemDetail> {
    return firstValueFrom(this.http.get<PortfolioItemDetail>(`/api/portfolio/${itemId}`));
  }

  async consider(candidate: {
    name: string;
    departmentId: string;
    priority: number;
    notes?: string;
  }): Promise<void> {
    await firstValueFrom(this.http.post('/api/portfolio/considered', candidate));
    this.reload();
  }

  async commit(
    itemId: string,
    decision: { projectId?: string; projectCode?: string; projectName?: string; decisionNotes: string },
  ): Promise<void> {
    await firstValueFrom(this.http.post(`/api/portfolio/${itemId}/commit`, decision));
    this.reload();
  }

  async activate(itemId: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/portfolio/${itemId}/activate`, null));
    this.reload();
  }

  async archive(itemId: string, reason: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/portfolio/${itemId}/archive`, { reason }));
    this.reload();
  }

  async revert(itemId: string, targetState: PortfolioState, reason: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/portfolio/${itemId}/revert`, { targetState, reason }));
    this.reload();
  }

  async addIteration(
    itemId: string,
    iteration: { name: string; length: IterationLength; startsOn: string; endsOn?: string },
  ): Promise<void> {
    await firstValueFrom(this.http.post(`/api/portfolio/${itemId}/iterations`, iteration));
    this.reload();
  }

  async closeIteration(itemId: string, iterationId: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/portfolio/${itemId}/iterations/${iterationId}/close`, null),
    );
    this.reload();
  }

  reload(): void {
    this.boardResource.reload();
  }
}

/**
 * The end date a preset implies, mirroring `Iteration.EndDateFor` on the server.
 *
 * Duplicated deliberately, and only to pre-fill the field as the user picks a preset — the server recomputes it
 * and its answer wins. Without this the quick selectors would show nothing until a round trip, which defeats the
 * point of a quick selector.
 */
export function endDateFor(length: IterationLength, startsOn: string): string {
  const start = new Date(`${startsOn}T00:00:00Z`);
  const end = new Date(start);

  switch (length) {
    case 'oneweek':
      end.setUTCDate(end.getUTCDate() + 6);
      break;
    case 'twoweeks':
      end.setUTCDate(end.getUTCDate() + 13);
      break;
    case 'onemonth':
      // Same clamping the server gets from AddMonths: 31 January plus a month lands at the end of February.
      end.setUTCDate(1);
      end.setUTCMonth(end.getUTCMonth() + 1);
      end.setUTCDate(
        Math.min(
          start.getUTCDate(),
          new Date(Date.UTC(end.getUTCFullYear(), end.getUTCMonth() + 1, 0)).getUTCDate(),
        ),
      );
      end.setUTCDate(end.getUTCDate() - 1);
      break;
    default:
      return startsOn;
  }

  return end.toISOString().slice(0, 10);
}
