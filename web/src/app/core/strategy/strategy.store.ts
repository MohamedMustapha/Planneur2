import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Mirrors `Cracra.Modules.Strategy.Contracts` (v2 §06). */

export type MetricKind = 'number' | 'percent' | 'currency' | 'milestone' | 'qualitative';

export type ObjectiveStatus = 'on-track' | 'at-risk' | 'off-track' | 'done';

export type StrategyStatus = 'draft' | 'active' | 'closed';

export type ContributionSource = 'item' | 'problem';

export const METRIC_KINDS: readonly MetricKind[] = [
  'number',
  'percent',
  'currency',
  'milestone',
  'qualitative',
];

export const OBJECTIVE_STATUSES: readonly ObjectiveStatus[] = [
  'on-track',
  'at-risk',
  'off-track',
  'done',
];

/** The two kinds where a number means something. Mirrors `MetricKinds.IsMeasured`. */
export function isMeasured(kind: MetricKind): boolean {
  return kind === 'number' || kind === 'percent' || kind === 'currency';
}

export interface StrategyView {
  readonly id: string;
  readonly scopeType: 'service' | 'node';
  readonly scopeId: string;
  readonly scopeName: string | null;
  readonly periodFrom: string;
  readonly periodTo: string;
  readonly title: string;
  readonly narrative: string | null;
  readonly ownerPersonId: string;
  readonly ownerName: string | null;
  readonly status: StrategyStatus;
  readonly objectiveCount: number;
  readonly progress: number;
}

export interface KeyResultView {
  readonly id: string;
  readonly title: string;
  readonly target: number;
  readonly current: number;
  readonly progress: number;
}

export interface ContributionView {
  readonly id: string;
  readonly sourceType: ContributionSource;
  readonly sourceId: string;
  readonly code: string | null;
  readonly name: string | null;
  readonly state: string | null;
  readonly weight: number;
  readonly note: string | null;
}

export interface ObjectiveView {
  readonly id: string;
  readonly strategyId: string;
  readonly title: string;
  readonly description: string | null;
  readonly metricKind: MetricKind;
  readonly baseline: number | null;
  readonly target: number | null;
  readonly current: number | null;
  readonly unit: string | null;
  readonly due: string | null;
  readonly status: ObjectiveStatus;
  readonly statusOverridden: boolean;
  readonly weight: number;
  readonly progress: number;
  readonly expectedProgress: number;
  readonly keyResults: readonly KeyResultView[];
  readonly contributions: readonly ContributionView[];
}

export interface StrategyRollup {
  readonly strategy: StrategyView;
  readonly objectives: readonly ObjectiveView[];
}

export interface UnlinkedItem {
  readonly itemId: string;
  readonly code: string;
  readonly name: string;
  readonly type: string;
  readonly state: string;
  readonly ownerNodeId: string;
}

export interface AlignmentGaps {
  readonly unlinkedObjectives: readonly ObjectiveView[];
  readonly unlinkedItems: readonly UnlinkedItem[];
}

/**
 * Strategy, as signals.
 *
 * No progress arithmetic here, deliberately. Every figure on these screens — an objective's progress, the line it
 * was expected to be on, a strategy's weighted total — arrives computed, because the same numbers are embedded in
 * a COPIL's minutes and printed in a report, and three implementations of "on track" is three answers.
 */
@Injectable({ providedIn: 'root' })
export class StrategyStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  /** Which strategy the overview is showing. Null means "the first one the server hands back". */
  readonly selectedId = signal<string | null>(null);

  private readonly listResource = httpResource<readonly StrategyView[]>(() =>
    this.session.isAuthenticated() ? '/api/strategy' : undefined,
  );

  readonly strategies = computed<readonly StrategyView[]>(() => this.listResource.value() ?? []);
  readonly isLoading = this.listResource.isLoading;

  /** The one on screen: whatever was picked, else the most recent active one, else the first. */
  readonly current = computed<StrategyView | null>(() => {
    const all = this.strategies();
    const picked = this.selectedId();

    if (picked) {
      return all.find((strategy) => strategy.id === picked) ?? null;
    }

    return all.find((strategy) => strategy.status === 'active') ?? all[0] ?? null;
  });

  private readonly rollupResource = httpResource<StrategyRollup>(() => {
    const strategy = this.current();

    return strategy ? `/api/strategy/${strategy.id}/rollup` : undefined;
  });

  readonly rollup = computed<StrategyRollup | null>(() => this.rollupResource.value() ?? null);
  readonly objectives = computed<readonly ObjectiveView[]>(() => this.rollup()?.objectives ?? []);

  /** The objectives a review should open on: off track first, then at risk. */
  readonly attention = computed(() =>
    this.objectives().filter(
      (objective) => objective.status === 'off-track' || objective.status === 'at-risk',
    ),
  );

  private readonly alignmentResource = httpResource<AlignmentGaps>(() =>
    this.session.isAuthenticated() ? '/api/strategy/alignment' : undefined,
  );

  readonly alignment = computed<AlignmentGaps | null>(() => this.alignmentResource.value() ?? null);

  refresh(): void {
    this.listResource.reload();
    this.rollupResource.reload();
    this.alignmentResource.reload();
  }

  async open(payload: {
    title: string;
    narrative: string | null;
    periodFrom: string | null;
    periodTo: string | null;
  }): Promise<string> {
    const created = await firstValueFrom(
      this.http.post<{ id: string }>('/api/strategy', payload),
    );

    this.selectedId.set(created.id);
    this.refresh();

    return created.id;
  }

  async amend(strategyId: string, payload: Record<string, unknown>): Promise<void> {
    await firstValueFrom(this.http.patch(`/api/strategy/${strategyId}`, payload));

    this.refresh();
  }

  async addObjective(
    strategyId: string,
    payload: {
      title: string;
      description: string | null;
      metricKind: MetricKind;
      baseline: number | null;
      target: number | null;
      unit: string | null;
      due: string | null;
      weight: number | null;
    },
  ): Promise<void> {
    await firstValueFrom(this.http.post(`/api/strategy/${strategyId}/objectives`, payload));

    this.refresh();
  }

  /** The monthly reading. Sent on its own so it can never be mistaken for moving the target. */
  async measure(objectiveId: string, current: number): Promise<void> {
    await firstValueFrom(this.http.patch(`/api/strategy/objectives/${objectiveId}`, { current }));

    this.refresh();
  }

  async setStatus(objectiveId: string, status: ObjectiveStatus | null): Promise<void> {
    await firstValueFrom(
      this.http.patch(`/api/strategy/objectives/${objectiveId}`, {
        status,
        clearStatusOverride: status === null,
      }),
    );

    this.refresh();
  }

  async removeObjective(objectiveId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/strategy/objectives/${objectiveId}`));

    this.refresh();
  }

  async link(
    objectiveId: string,
    sourceType: ContributionSource,
    sourceId: string,
    note: string | null,
  ): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/strategy/objectives/${objectiveId}/contributions`, {
        sourceType,
        sourceId,
        note,
      }),
    );

    this.refresh();
  }

  async unlink(objectiveId: string, contributionId: string): Promise<void> {
    await firstValueFrom(
      this.http.delete(`/api/strategy/objectives/${objectiveId}/contributions/${contributionId}`),
    );

    this.refresh();
  }
}
