import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Mirrors `Cracra.Modules.Meetings.Contracts`. */

export type MeetingScopeType = 'unit' | 'department' | 'project' | 'org';

export type SpecialDaySeverity = 'info' | 'warning' | 'critical';

export type AttendanceResponse = 'accepted' | 'declined' | 'tentative';

/**
 * How wide a meeting reaches (v2 §07.1).
 *
 * Not the same question as the scope, which says who it targets. The scope decides who may see the meeting; the
 * level decides how its CR is distributed and how a brief composes upward.
 */
export type MeetingLevel = 'unit' | 'node' | 'cross-node' | 'service' | 'project';

export const MEETING_LEVELS: readonly MeetingLevel[] = [
  'unit',
  'node',
  'cross-node',
  'service',
  'project',
];

export interface MeetingSeriesView {
  readonly id: string;
  readonly kind: string;
  /** A Transloco key or free text — the client renders whichever resolves. */
  readonly nameKey: string;
  readonly scopeType: MeetingScopeType;
  readonly scopeId: string | null;
  /** iCal RRULE, e.g. `FREQ=WEEKLY;BYDAY=MO`. */
  readonly recurrenceRule: string;
  readonly startsOn: string;
  readonly startTime: string;
  readonly timeZoneId: string;
  readonly durationMinutes: number;
  readonly ownerPersonId: string;
  readonly ownerName: string | null;
  readonly location: string | null;
  readonly videoLink: string | null;
  readonly active: boolean;
  readonly level: MeetingLevel;
  /** The child nodes a cross-node series brings together. Empty at every other level. */
  readonly scopeIds: readonly string[];
}

export interface MeetingOccurrenceView {
  readonly id: string;
  readonly seriesId: string;
  readonly kind: string;
  readonly nameKey: string;
  readonly scopeType: MeetingScopeType;
  readonly scopeId: string | null;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly status: string;
  readonly location: string | null;
  readonly videoLink: string | null;
  readonly notesRef: string | null;
  readonly myResponse: AttendanceResponse | null;
  readonly level: MeetingLevel;
  /** Null until somebody opens the CR, which is what the "write it" control keys off. */
  readonly minutesId: string | null;
  readonly minutesPublished: boolean;
}

export interface SpecialDayView {
  readonly id: string;
  readonly kind: string;
  readonly nameKey: string;
  readonly scopeType: MeetingScopeType;
  readonly scopeId: string | null;
  readonly date: string;
  readonly allDay: boolean;
  readonly severity: SpecialDaySeverity;
  readonly description: string | null;
}

export interface UpcomingEntry {
  readonly id: string;
  readonly kind: string;
  readonly nameKey: string;
  readonly scopeType: MeetingScopeType;
  readonly at: string;
  readonly allDay: boolean;
  readonly severity: SpecialDaySeverity | null;
  readonly location: string | null;
  readonly videoLink: string | null;
}

export interface MeetingSeriesPayload {
  readonly kind: string;
  readonly nameKey: string;
  readonly scopeType: MeetingScopeType;
  readonly scopeId: string | null;
  readonly recurrenceRule: string;
  readonly startsOn: string;
  readonly startTime: string;
  readonly durationMinutes: number;
  readonly location: string | null;
  readonly videoLink: string | null;
  readonly active: boolean;
  readonly level: MeetingLevel;
  readonly scopeIds: readonly string[];
}

export interface SpecialDayPayload {
  readonly kind: string;
  readonly nameKey: string;
  readonly scopeType: MeetingScopeType;
  readonly scopeId: string | null;
  readonly date: string;
  readonly allDay: boolean;
  readonly severity: SpecialDaySeverity;
  readonly description: string | null;
}

export const MEETING_KINDS = [
  'weekly',
  'weekly-node',
  'copil',
  'service-review',
  'retro',
  'one-on-one',
  'custom',
] as const;

export const SPECIAL_DAY_KINDS = [
  'patch-party',
  'audit',
  'go-live',
  'deadline',
  'freeze',
  'holiday',
  'custom',
] as const;

export const SPECIAL_DAY_SEVERITIES: readonly SpecialDaySeverity[] = [
  'info',
  'warning',
  'critical',
];

/**
 * Meetings and special days, as signals.
 *
 * One store for both, because the two are one screen and one strip: what a manager edits separately, everybody
 * else reads merged. Splitting it would mean two stores that both have to know what "upcoming" means.
 */
@Injectable({ providedIn: 'root' })
export class MeetingsStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  /** How far ahead the dashboard strip looks. A fortnight is what the server defaults to. */
  readonly upcomingDays = signal(14);

  readonly includeInactive = signal(false);

  private readonly upcomingResource = httpResource<readonly UpcomingEntry[]>(() =>
    this.session.isAuthenticated()
      ? `/api/meetings/upcoming?days=${this.upcomingDays()}`
      : undefined,
  );

  private readonly seriesResource = httpResource<readonly MeetingSeriesView[]>(() =>
    this.session.isAuthenticated()
      ? `/api/meetings/series?includeInactive=${this.includeInactive()}`
      : undefined,
  );

  private readonly specialDaysResource = httpResource<readonly SpecialDayView[]>(() =>
    this.session.isAuthenticated() ? '/api/meetings/special-days' : undefined,
  );

  readonly upcoming = computed<readonly UpcomingEntry[]>(() => this.upcomingResource.value() ?? []);
  readonly series = computed<readonly MeetingSeriesView[]>(() => this.seriesResource.value() ?? []);
  readonly specialDays = computed<readonly SpecialDayView[]>(
    () => this.specialDaysResource.value() ?? [],
  );

  readonly isLoading = computed(
    () => this.seriesResource.isLoading() || this.specialDaysResource.isLoading(),
  );

  // A strip that failed to load is not the same as an empty calendar, and the difference matters: the first
  // needs a message, the second is just a quiet fortnight.
  readonly upcomingError = computed(() => this.upcomingResource.error());

  async createSeries(payload: MeetingSeriesPayload): Promise<void> {
    await firstValueFrom(this.http.post('/api/meetings/series', payload));
    this.reload();
  }

  async updateSeries(id: string, payload: MeetingSeriesPayload): Promise<void> {
    await firstValueFrom(this.http.put(`/api/meetings/series/${id}`, payload));
    this.reload();
  }

  async deleteSeries(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/meetings/series/${id}`));
    this.reload();
  }

  async createSpecialDay(payload: SpecialDayPayload): Promise<void> {
    await firstValueFrom(this.http.post('/api/meetings/special-days', payload));
    this.reload();
  }

  async deleteSpecialDay(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/meetings/special-days/${id}`));
    this.reload();
  }

  async respond(occurrenceId: string, response: AttendanceResponse): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/meetings/occurrences/${occurrenceId}/respond`, { response }),
    );

    this.upcomingResource.reload();
  }

  /** The occurrences of one window, as a resource, because the meetings page watches its own fortnight. */
  readonly windowFrom = signal(asDate(new Date()));
  readonly windowTo = signal(asDate(new Date(Date.now() + 13 * 86_400_000)));

  private readonly windowResource = httpResource<readonly MeetingOccurrenceView[]>(() =>
    this.session.isAuthenticated()
      ? `/api/meetings/occurrences?from=${this.windowFrom()}&to=${this.windowTo()}`
      : undefined,
  );

  readonly window = computed<readonly MeetingOccurrenceView[]>(
    () => this.windowResource.value() ?? [],
  );

  readonly windowLoading = this.windowResource.isLoading;

  reloadWindow(): void {
    this.windowResource.reload();
  }

  /** The occurrences of one window, asked for once. The manager asks for a window; it does not watch one. */
  async occurrences(from: Date, to: Date): Promise<readonly MeetingOccurrenceView[]> {
    return firstValueFrom(
      this.http.get<MeetingOccurrenceView[]>(
        `/api/meetings/occurrences?from=${asDate(from)}&to=${asDate(to)}`,
      ),
    );
  }

  reload(): void {
    this.seriesResource.reload();
    this.specialDaysResource.reload();
    this.upcomingResource.reload();
  }
}

/**
 * An RRULE from the parts a picker offers.
 *
 * Built here rather than typed by hand, because RRULE is a format for calendars to exchange rather than for
 * people to write — but stored as RRULE all the same, so that an export or an import one day carries the rule
 * rather than a re-derivation of it.
 */
export function buildRecurrenceRule(
  frequency: 'DAILY' | 'WEEKLY' | 'MONTHLY',
  interval: number,
  weekdays: readonly string[],
): string {
  const parts = [`FREQ=${frequency}`];

  if (interval > 1) {
    parts.push(`INTERVAL=${interval}`);
  }

  if (frequency === 'WEEKLY' && weekdays.length > 0) {
    parts.push(`BYDAY=${weekdays.join(',')}`);
  }

  return parts.join(';');
}

/** The weekday codes RRULE uses, in the order the org's week runs. */
export const WEEKDAY_CODES = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU'] as const;

function asDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}
