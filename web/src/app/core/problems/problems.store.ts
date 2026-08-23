import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Mirrors `Cracra.Modules.Problems.Contracts` (v2 §05). */

export type ProblemCategory =
  | 'work-process'
  | 'project'
  | 'quality-of-life'
  | 'tooling'
  | 'data'
  | 'other';

export type ImpactFrequency = 'daily' | 'weekly' | 'monthly' | 'occasional';

export type ProblemStatus =
  | 'new'
  | 'triaged'
  | 'accepted'
  | 'converted'
  | 'resolved'
  | 'declined'
  | 'duplicate';

export const PROBLEM_CATEGORIES: readonly ProblemCategory[] = [
  'work-process',
  'project',
  'quality-of-life',
  'tooling',
  'data',
  'other',
];

export const FREQUENCIES: readonly ImpactFrequency[] = ['daily', 'weekly', 'monthly', 'occasional'];

export interface ProblemCard {
  readonly id: string;
  readonly code: string;
  readonly title: string;
  readonly description: string | null;
  readonly category: ProblemCategory;
  readonly nodeId: string;
  readonly reporterPersonId: string;
  readonly reporterName: string | null;
  readonly impactTimeLoss: number | null;
  readonly impactFrequency: ImpactFrequency;
  readonly affectedPeopleEstimate: number | null;
  readonly annualHoursLost: number;
  readonly voteCount: number;
  readonly proposalCount: number;
  readonly votedByMe: boolean;
  readonly status: ProblemStatus;
  readonly convertedItemId: string | null;
  readonly decisionReason: string | null;
  readonly createdAt: string;
}

export interface ProposalView {
  readonly id: string;
  readonly authorPersonId: string;
  readonly authorName: string | null;
  readonly description: string;
  readonly effortGuess: number | null;
  readonly createdAt: string;
}

export interface ProblemDetail {
  readonly card: ProblemCard;
  readonly proposals: readonly ProposalView[];
  readonly comments: readonly {
    readonly id: string;
    readonly authorName: string | null;
    readonly body: string;
  }[];
}

export interface FiledProblem {
  readonly id: string;
  readonly suggestions: readonly ProblemCard[];
}

export type ProblemSort = 'impact' | 'votes' | 'recent';

/**
 * Problems, as signals.
 *
 * Sorting is the server's, not the client's: impact is time lost times frequency times reach, and recomputing it
 * here would give the board and the API two rankings that agree until somebody changes one of them.
 */
@Injectable({ providedIn: 'root' })
export class ProblemsStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  readonly sort = signal<ProblemSort>('impact');
  readonly category = signal<ProblemCategory | null>(null);
  readonly status = signal<ProblemStatus | null>(null);

  private readonly listResource = httpResource<readonly ProblemCard[]>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const params = new URLSearchParams();

    params.set('sort', this.sort());

    const category = this.category();
    const status = this.status();

    if (category) {
      params.set('category', category);
    }

    if (status) {
      params.set('status', status);
    }

    return `/api/problems?${params.toString()}`;
  });

  readonly problems = computed<readonly ProblemCard[]>(() => this.listResource.value() ?? []);
  readonly isLoading = this.listResource.isLoading;

  readonly mine = computed(() => {
    const me = this.session.user().id;

    return this.problems().filter((problem) => problem.reporterPersonId === me);
  });

  /** Accepted but not yet converted — the incoming pipeline a solving branch works from. */
  readonly incoming = computed(() =>
    this.problems().filter((problem) => problem.status === 'accepted'),
  );

  refresh(): void {
    this.listResource.reload();
  }

  async file(payload: {
    title: string;
    description: string | null;
    category: ProblemCategory;
    impactTimeLoss: number | null;
    impactFrequency: ImpactFrequency;
    affectedPeopleEstimate: number | null;
  }): Promise<FiledProblem> {
    const filed = await firstValueFrom(this.http.post<FiledProblem>('/api/problems', payload));

    this.refresh();

    return filed;
  }

  async get(problemId: string): Promise<ProblemDetail> {
    return firstValueFrom(this.http.get<ProblemDetail>(`/api/problems/${problemId}`));
  }

  async search(query: string): Promise<readonly ProblemCard[]> {
    const trimmed = query.trim();

    if (trimmed.length < 3) {
      return [];
    }

    return firstValueFrom(
      this.http.get<readonly ProblemCard[]>(`/api/problems/search?q=${encodeURIComponent(trimmed)}`),
    );
  }

  async vote(problemId: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/problems/${problemId}/vote`, {}));

    this.refresh();
  }

  async propose(problemId: string, description: string, effortGuess: number | null): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/problems/${problemId}/proposals`, { description, effortGuess }),
    );
  }

  async triage(problemId: string, decision: string, reason: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`/api/problems/${problemId}/triage`, { decision, reason }));

    this.refresh();
  }

  async convert(problemId: string, type: string): Promise<string> {
    const created = await firstValueFrom(
      this.http.post<{ id: string }>(`/api/problems/${problemId}/convert`, { type }),
    );

    this.refresh();

    return created.id;
  }
}
