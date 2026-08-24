import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { zonedDay } from '../../core/time/zoned';
import {
  buildRecurrenceRule,
  MEETING_KINDS,
  MEETING_LEVELS,
  MeetingLevel,
  MeetingScopeType,
  MeetingSeriesView,
  MeetingsStore,
  SPECIAL_DAY_KINDS,
  SPECIAL_DAY_SEVERITIES,
  SpecialDaySeverity,
  WEEKDAY_CODES,
} from '../../core/meetings/meetings.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SettingsTabs } from '../directory/settings-tabs';

type Frequency = 'DAILY' | 'WEEKLY' | 'MONTHLY';

/**
 * The meeting manager and the special-day manager, on one screen.
 *
 * Both halves are the same job — "what does this scope's calendar look like" — and both are done by the same
 * people in the same sitting. Two routes would mean two scope pickers that drift apart.
 *
 * It lives behind the settings tabs rather than in the rail: the design fixes the rail at ten sections, and this
 * is administration. The dashboard's "coming up" strip is where everybody else meets the same data.
 */
@Component({
  selector: 'app-meeting-manager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, PageHeader, SettingsTabs],
  templateUrl: './meeting-manager.html',
  styleUrl: './meeting-manager.scss',
})
export class MeetingManager {
  protected readonly meetings = inject(MeetingsStore);
  private readonly preferences = inject(PreferencesStore);
  protected readonly directory = inject(DirectoryStore);

  protected readonly kinds = MEETING_KINDS;
  protected readonly specialDayKinds = SPECIAL_DAY_KINDS;
  protected readonly severities = SPECIAL_DAY_SEVERITIES;
  protected readonly weekdays = WEEKDAY_CODES;

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  // --- The series form ------------------------------------------------------------------------------------------

  protected readonly kind = signal<string>('weekly');
  protected readonly name = signal('');
  protected readonly scopeType = signal<MeetingScopeType>('unit');
  protected readonly scopeId = signal('');
  protected readonly level = signal<MeetingLevel>('node');
  protected readonly crossNodeIds = signal<readonly string[]>([]);
  protected readonly frequency = signal<Frequency>('WEEKLY');
  protected readonly interval = signal(1);
  protected readonly selectedDays = signal<readonly string[]>(['MO']);
  protected readonly startsOn = signal(today(this.preferences.timeZone()));
  protected readonly startTime = signal('09:00');
  protected readonly durationMinutes = signal(30);
  protected readonly location = signal('');
  protected readonly videoLink = signal('');

  /**
   * The rule the picker composes, shown as it is built.
   *
   * RRULE is a format calendars exchange, not one people write, so the form offers frequency, interval and
   * weekdays — and then shows the string, because somebody who does know RRULE should be able to check it rather
   * than having to save and re-open to find out what the form meant.
   */
  protected readonly recurrenceRule = computed(() =>
    buildRecurrenceRule(this.frequency(), this.interval(), this.selectedDays()),
  );

  protected readonly weekdaysApply = computed(() => this.frequency() === 'WEEKLY');

  /** Unit and department targets come from the directory; the org target has no id to pick. */
  protected readonly scopeOptions = computed(() => {
    if (this.scopeType() === 'unit') {
      return this.directory.visibleUnits().map((unit) => ({ id: unit.id, label: unit.name }));
    }

    if (this.scopeType() === 'department') {
      return this.directory.departments().map((department) => ({
        id: department.id,
        label: department.code.toUpperCase(),
      }));
    }

    return [];
  });

  protected readonly needsScopeId = computed(() => this.scopeType() !== 'org');

  protected readonly levels = MEETING_LEVELS;

  /** Only a cross-node series names other nodes, and only then is the picker worth showing (v2 §07.1). */
  protected readonly isCrossNode = computed(() => this.level() === 'cross-node');

  protected readonly crossNodeOptions = computed(() =>
    this.directory.visibleUnits().map((unit) => ({ id: unit.id, label: unit.name })),
  );

  // --- The special-day form -------------------------------------------------------------------------------------

  protected readonly dayKind = signal<string>('patch-party');
  protected readonly dayName = signal('');
  protected readonly dayScopeType = signal<MeetingScopeType>('department');
  protected readonly dayScopeId = signal('');
  protected readonly dayDate = signal(today(this.preferences.timeZone()));
  protected readonly daySeverity = signal<SpecialDaySeverity>('warning');
  protected readonly dayDescription = signal('');

  protected readonly dayScopeOptions = computed(() => {
    if (this.dayScopeType() === 'unit') {
      return this.directory.visibleUnits().map((unit) => ({ id: unit.id, label: unit.name }));
    }

    if (this.dayScopeType() === 'department') {
      return this.directory.departments().map((department) => ({
        id: department.id,
        label: department.code.toUpperCase(),
      }));
    }

    return [];
  });

  protected readonly dayNeedsScopeId = computed(() => this.dayScopeType() !== 'org');

  // --- Actions --------------------------------------------------------------------------------------------------

  protected setLevel(value: string): void {
    this.level.set(value as MeetingLevel);

    if (!this.isCrossNode()) {
      this.crossNodeIds.set([]);
    }
  }

  protected toggleCrossNode(nodeId: string): void {
    this.crossNodeIds.update((ids) =>
      ids.includes(nodeId) ? ids.filter((id) => id !== nodeId) : [...ids, nodeId],
    );
  }

  protected toggleDay(code: string): void {
    this.selectedDays.update((days) =>
      days.includes(code) ? days.filter((day) => day !== code) : [...days, code],
    );
  }

  protected async createSeries(): Promise<void> {
    if (!this.name() || (this.needsScopeId() && !this.scopeId())) {
      return;
    }

    // Refused here as well as by the server, because the server's refusal arrives as a banner and this one
    // arrives as a disabled button beside the picker that is missing an answer.
    if (this.isCrossNode() && this.crossNodeIds().length === 0) {
      return;
    }

    await this.run(async () => {
      await this.meetings.createSeries({
        kind: this.kind(),
        nameKey: this.name(),
        scopeType: this.scopeType(),
        scopeId: this.needsScopeId() ? this.scopeId() : null,
        recurrenceRule: this.recurrenceRule(),
        startsOn: this.startsOn(),
        // The server wants a whole time; the browser's time input gives HH:mm.
        startTime: `${this.startTime()}:00`,
        durationMinutes: this.durationMinutes(),
        location: this.location() || null,
        videoLink: this.videoLink() || null,
        active: true,
        level: this.level(),
        scopeIds: this.isCrossNode() ? this.crossNodeIds() : [],
      });

      this.name.set('');
      this.location.set('');
      this.videoLink.set('');
    });
  }

  /** Deactivation rather than deletion: a series that ran and stopped is history somebody asks about. */
  protected async deactivate(series: MeetingSeriesView): Promise<void> {
    await this.run(() =>
      this.meetings.updateSeries(series.id, {
        kind: series.kind,
        nameKey: series.nameKey,
        scopeType: series.scopeType,
        scopeId: series.scopeId,
        recurrenceRule: series.recurrenceRule,
        startsOn: series.startsOn,
        startTime: series.startTime,
        durationMinutes: series.durationMinutes,
        location: series.location,
        videoLink: series.videoLink,
        active: !series.active,
        level: series.level,
        scopeIds: series.scopeIds,
      }),
    );
  }

  protected async createSpecialDay(): Promise<void> {
    if (!this.dayName() || (this.dayNeedsScopeId() && !this.dayScopeId())) {
      return;
    }

    await this.run(async () => {
      await this.meetings.createSpecialDay({
        kind: this.dayKind(),
        nameKey: this.dayName(),
        scopeType: this.dayScopeType(),
        scopeId: this.dayNeedsScopeId() ? this.dayScopeId() : null,
        date: this.dayDate(),
        allDay: true,
        severity: this.daySeverity(),
        description: this.dayDescription() || null,
      });

      this.dayName.set('');
      this.dayDescription.set('');
    });
  }

  protected async deleteSpecialDay(id: string): Promise<void> {
    await this.run(() => this.meetings.deleteSpecialDay(id));
  }

  /**
   * Runs an action and surfaces the refusal.
   *
   * Every one of these can legitimately be refused — a scope this person does not own, a rule the parser will not
   * take — and the server's own sentence says which rule and what would fix it, so it is shown rather than
   * replaced with a generic apology.
   */
  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (failure: unknown) {
      const problem = failure as { error?: { detail?: string; title?: string } };

      this.error.set(problem.error?.detail ?? problem.error?.title ?? 'meetings.genericError');
    } finally {
      this.busy.set(false);
    }
  }
}

/**
 * Today, on the reader's own calendar.
 *
 * Was `toISOString().slice(0, 10)`, which is the UTC day: a picker opened at 01:00 in Paris defaulted to
 * yesterday, and one opened in the evening in New York defaulted to tomorrow.
 */
function today(zone: string): string {
  return zonedDay(new Date(), zone);
}
