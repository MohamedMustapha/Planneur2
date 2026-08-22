import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { DEFAULT_WORKING_DAY, WorkingDay } from '../time/working-day';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { isoWeekNumber, startOfWeek } from '../time/week';

/** Mirrors `Cracra.Modules.Activities.Contracts`. */

export type ActivityKind = 'planned' | 'actual';

export type ActivitySource = 'manual' | 'azure-devops' | 'servicenow';

export interface ActivityTypeOption {
  readonly code: string;
  readonly parentCode: string | null;
  readonly labelKey: string;
  readonly requiresProject: boolean;
}

export interface ActivityEntryView {
  readonly id: string;
  readonly personId: string;
  readonly personName: string | null;
  readonly unitId: string;
  readonly departmentId: string;
  readonly activityTypeCode: string;
  readonly activityTypeLabelKey: string;
  readonly projectId: string | null;
  readonly projectCode: string | null;
  readonly iterationId: string | null;
  readonly kind: ActivityKind;
  readonly source: ActivitySource;
  readonly externalRef: string | null;
  readonly slotStart: string;
  readonly slotEnd: string;
  readonly hours: number;
  readonly supersedesEntryId: string | null;
  readonly reconciled: boolean;
  readonly note: string | null;
  readonly percentComplete: number | null;
}

export interface WeeklyTypeTotal {
  readonly activityTypeCode: string;
  readonly labelKey: string;
  readonly plannedHours: number;
  readonly actualHours: number;
}

export interface WeeklySummary {
  readonly isoYear: number;
  readonly isoWeek: number;
  readonly monday: string;
  readonly sunday: string;
  readonly targetHours: number;
  readonly enforced: boolean;
  readonly actualHours: number;
  readonly plannedHours: number;
  readonly overtime: number;
  /** within | warned | blocked */
  readonly status: string;
  readonly byType: readonly WeeklyTypeTotal[];
  /** Null where the server predates the field; the store defaults it rather than each caller. */
  readonly workingDay: WorkingDay | null;
}

export interface AssignableTask {
  readonly source: ActivitySource;
  readonly externalRef: string;
  readonly title: string;
  readonly state: string | null;
  readonly projectId: string | null;
  readonly suggestedActivityTypeCode: string | null;
}

export interface LogActivityResult {
  readonly id: string;
  readonly guardrailStatus: string;
  readonly weekHours: number;
  readonly targetHours: number;
  readonly overtime: number;
  readonly reconciledPlanId: string | null;
}

export interface LogActivityInput {
  readonly personId?: string;
  readonly activityTypeCode: string;
  readonly projectId?: string | null;
  readonly iterationId?: string | null;
  readonly kind: ActivityKind;
  readonly source?: ActivitySource;
  readonly externalRef?: string | null;
  readonly slotStart: string;
  readonly slotEnd: string;
  readonly hours?: number;
  readonly note?: string | null;
}

/**
 * Activities, as signals.
 *
 * The week being viewed is the store's own state rather than each screen's, because the meter, the feed and the
 * summary must always agree about which week they are describing — three components each holding their own offset
 * is how a board ends up showing Tuesday's hours against last week's target.
 */
@Injectable({ providedIn: 'root' })
export class ActivitiesStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  /** Weeks from the current one. Negative is the past. */
  readonly weekOffset = signal(0);

  readonly week = computed(() => {
    const monday = startOfWeek(new Date());
    monday.setDate(monday.getDate() + this.weekOffset() * 7);

    return { isoWeek: isoWeekNumber(monday), monday, sunday: addDays(monday, 6) };
  });

  private readonly weekParam = computed(() => {
    const { monday, isoWeek } = this.week();

    // The ISO year, not the calendar one: 1 January can belong to week 53 of the year before, and asking for
    // "2027-W53" would be a week that does not exist.
    return `${isoYear(monday)}-W${String(isoWeek).padStart(2, '0')}`;
  });

  private readonly summaryResource = httpResource<WeeklySummary>(() =>
    this.session.isAuthenticated()
      ? `/api/activities/weekly-summary/me?week=${this.weekParam()}`
      : undefined,
  );

  private readonly feedResource = httpResource<ActivityEntryView[]>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const { monday, sunday } = this.week();

    return `/api/activities?scope=me&from=${asDate(monday)}&to=${asDate(sunday)}`;
  });

  private readonly typesResource = httpResource<ActivityTypeOption[]>(() =>
    this.session.isAuthenticated() ? '/api/activities/types' : undefined,
  );

  readonly summary = computed(() => this.summaryResource.value());
  readonly entries = computed<readonly ActivityEntryView[]>(() => this.feedResource.value() ?? []);
  readonly types = computed<readonly ActivityTypeOption[]>(() => this.typesResource.value() ?? []);
  readonly isLoading = computed(
    () => this.summaryResource.isLoading() || this.feedResource.isLoading(),
  );

  readonly targetHours = computed(() => this.summary()?.targetHours ?? 35);

  /**
   * The department's working day, or the platform's where it has configured none.
   *
   * Defaulted here rather than at each caller: the board's axis, the quick-add's presets and the timeline's clamp
   * all need the same answer, and three copies of the fallback is three chances for one of them to draw an axis
   * the other two do not agree with.
   */
  readonly workingDay = computed<WorkingDay>(() => this.summary()?.workingDay ?? DEFAULT_WORKING_DAY);

  /**
   * A full day, derived from the department's own weekly target rather than hard-coded at seven.
   *
   * Five working days, because the boards run Monday to Friday and the target is expressed against that week. A
   * department on 32 hours gets 6.4, not the statutory 7 — the whole point of the target being configuration is
   * that nothing downstream assumes the default.
   */
  readonly dailyTargetHours = computed(() => Math.round((this.targetHours() / 5) * 4) / 4);
  readonly loggedHours = computed(() => this.summary()?.actualHours ?? 0);
  readonly plannedHours = computed(() => this.summary()?.plannedHours ?? 0);
  readonly overtime = computed(() => this.summary()?.overtime ?? 0);

  /** True where the department made the target a hard limit rather than a warning. */
  readonly isEnforced = computed(() => this.summary()?.enforced ?? false);
  readonly isOverTarget = computed(() => (this.summary()?.status ?? 'within') !== 'within');

  /** Hours by canonical bucket, for the summary tiles. */
  hoursFor(code: string): number {
    return (
      this.summary()?.byType.find((total) => total.activityTypeCode === code)?.actualHours ?? 0
    );
  }

  stepWeek(by: number): void {
    this.weekOffset.update((offset) => offset + by);
  }

  goToCurrentWeek(): void {
    this.weekOffset.set(0);
  }

  async log(input: LogActivityInput): Promise<LogActivityResult> {
    const result = await firstValueFrom(
      this.http.post<LogActivityResult>('/api/activities', {
        source: 'manual',
        ...input,
      }),
    );

    this.reload();

    return result;
  }

  async remove(entryId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/activities/${entryId}`));
    this.reload();
  }

  async assignableTasks(source?: ActivitySource): Promise<readonly AssignableTask[]> {
    const query = source ? `?source=${source}` : '';

    return firstValueFrom(
      this.http.get<AssignableTask[]>(`/api/activities/assignable-tasks${query}`),
    );
  }

  reload(): void {
    this.summaryResource.reload();
    this.feedResource.reload();
  }
}

function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);

  return result;
}

function asDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

/**
 * The ISO week-numbering year, which is not always the calendar year.
 *
 * Mirrors `System.Globalization.ISOWeek.GetYear` on the server. The Thursday of a week always falls in that week's
 * ISO year, which is the whole trick.
 */
function isoYear(monday: Date): number {
  const thursday = addDays(monday, 3);

  return thursday.getFullYear();
}
