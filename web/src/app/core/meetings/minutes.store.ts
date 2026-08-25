import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { MeetingLevel } from './meetings.store';

/** Mirrors the minutes half of `Cracra.Modules.Meetings.Contracts` (v2 §07). */

export type ActionLinkType = 'none' | 'problem' | 'item' | 'objective';

export type ActionStatus = 'open' | 'done' | 'dropped';

export const ACTION_LINK_TYPES: readonly ActionLinkType[] = ['none', 'problem', 'item', 'objective'];

export interface DecisionView {
  readonly id: string;
  readonly text: string;
  readonly rationale: string | null;
  readonly decidedBy: string | null;
}

export interface ActionItemView {
  readonly id: string;
  readonly minutesId: string;
  readonly title: string;
  readonly ownerPersonId: string;
  readonly ownerName: string | null;
  readonly due: string | null;
  readonly status: ActionStatus;
  readonly linkType: ActionLinkType;
  readonly linkId: string | null;
  /** The server's answer, against the clock the tracker sorts by. Never recomputed here. */
  readonly overdue: boolean;
}

export interface MinutesView {
  readonly id: string;
  readonly occurrenceId: string;
  readonly seriesId: string;
  readonly level: MeetingLevel;
  readonly scopeType: string;
  readonly scopeId: string | null;
  readonly occurredAt: string;
  readonly authorPersonId: string;
  readonly authorName: string | null;
  readonly agenda: string | null;
  readonly attendees: readonly string[];
  readonly absentees: readonly string[];
  readonly summary: string | null;
  readonly published: boolean;
  readonly publishedAt: string | null;
  readonly decisions: readonly DecisionView[];
  readonly actions: readonly ActionItemView[];
}

export interface MinutesDigest {
  readonly id: string;
  readonly occurrenceId: string;
  readonly level: MeetingLevel;
  readonly nameKey: string;
  readonly occurredAt: string;
  readonly summary: string | null;
  readonly decisionCount: number;
  readonly openActionCount: number;
  readonly published: boolean;
}

export type TrackerOwner = 'me' | 'scope';

export type TrackerStatus = 'open' | 'overdue' | 'all';

/**
 * The compte-rendu and its action tracker, as signals.
 *
 * Separate from `MeetingsStore` on purpose. That store answers "what is on the calendar"; this one answers "what
 * came out of it", and the two are read by different screens at different moments — a strip that reloads because
 * somebody edited a recurrence rule would be redrawing a CR nobody touched.
 */
@Injectable({ providedIn: 'root' })
export class MinutesStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  readonly trackerOwner = signal<TrackerOwner>('me');
  readonly trackerStatus = signal<TrackerStatus>('open');

  private readonly latestResource = httpResource<readonly MinutesDigest[]>(() =>
    this.session.isAuthenticated() ? '/api/meetings/minutes' : undefined,
  );

  private readonly actionsResource = httpResource<readonly ActionItemView[]>(() =>
    this.session.isAuthenticated()
      ? `/api/meetings/actions?owner=${this.trackerOwner()}&status=${this.trackerStatus()}`
      : undefined,
  );

  readonly latest = computed<readonly MinutesDigest[]>(() => this.latestResource.value() ?? []);
  readonly actions = computed<readonly ActionItemView[]>(() => this.actionsResource.value() ?? []);

  readonly isLoading = computed(
    () => this.latestResource.isLoading() || this.actionsResource.isLoading(),
  );

  readonly overdueCount = computed(() => this.actions().filter((action) => action.overdue).length);

  reload(): void {
    this.latestResource.reload();
    this.actionsResource.reload();
  }

  /** Starts the CR, or reopens the draft somebody already started. Both are the same request. */
  async open(occurrenceId: string): Promise<MinutesView> {
    const minutes = await firstValueFrom(
      this.http.post<MinutesView>(`/api/meetings/occurrences/${occurrenceId}/minutes`, {}),
    );

    this.reload();

    return minutes;
  }

  async get(occurrenceId: string): Promise<MinutesView> {
    return firstValueFrom(
      this.http.get<MinutesView>(`/api/meetings/occurrences/${occurrenceId}/minutes`),
    );
  }

  async amend(
    minutesId: string,
    patch: {
      agenda?: string | null;
      attendees?: readonly string[];
      absentees?: readonly string[];
      summary?: string | null;
    },
  ): Promise<MinutesView> {
    return firstValueFrom(this.http.patch<MinutesView>(`/api/meetings/minutes/${minutesId}`, patch));
  }

  /**
   * Asks the on-prem model for a first draft of the summary.
   *
   * Returns the text and saves nothing — the author edits it and presses save, exactly as they would with their
   * own words. A CR nobody read before it was published is worse than a CR nobody wrote.
   */
  async draft(minutesId: string): Promise<string> {
    const drafted = await firstValueFrom(
      this.http.post<{ text: string }>(`/api/meetings/minutes/${minutesId}/draft`, {}),
    );

    return drafted.text;
  }

  async publish(minutesId: string): Promise<MinutesView> {
    const published = await firstValueFrom(
      this.http.post<MinutesView>(`/api/meetings/minutes/${minutesId}/publish`, {}),
    );

    this.reload();

    return published;
  }

  async decide(
    minutesId: string,
    decision: { text: string; rationale: string | null; decidedBy: string | null },
  ): Promise<MinutesView> {
    return firstValueFrom(
      this.http.post<MinutesView>(`/api/meetings/minutes/${minutesId}/decisions`, decision),
    );
  }

  async assign(
    minutesId: string,
    action: {
      title: string;
      ownerPersonId: string | null;
      due: string | null;
      linkType: ActionLinkType;
      linkId: string | null;
    },
  ): Promise<MinutesView> {
    const saved = await firstValueFrom(
      this.http.post<MinutesView>(`/api/meetings/minutes/${minutesId}/actions`, action),
    );

    this.reload();

    return saved;
  }

  async settle(minutesId: string, actionId: string, status: ActionStatus): Promise<MinutesView> {
    const saved = await firstValueFrom(
      this.http.patch<MinutesView>(`/api/meetings/minutes/${minutesId}/actions/${actionId}`, {
        status,
      }),
    );

    this.reload();

    return saved;
  }
}
