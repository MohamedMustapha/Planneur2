import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  ActivitiesStore,
  ActivityKind,
  ActivitySource,
  AssignableTask,
} from '../../core/activities/activities.store';
import { ProjectsStore } from '../../core/projects/projects.store';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { instantOf, zonedDay } from '../../core/time/zoned';
import { DaySession, SessionId, hoursBetween, sessionsOf } from '../../core/time/working-day';

/**
 * Logging one slot.
 *
 * The spec asks for two input modes and this is both of them: type it in, or pull a task from Azure DevOps or
 * ServiceNow and let it fill in the project, the type and the reference. The pulled path is the same form with
 * three fields already answered — not a separate flow — because what the user is doing is identical either way.
 */
@Component({
  selector: 'app-quick-add',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule],
  templateUrl: './quick-add.html',
  styleUrl: './quick-add.scss',
})
export class QuickAdd {
  private readonly activities = inject(ActivitiesStore);
  protected readonly projects = inject(ProjectsStore);
  private readonly preferences = inject(PreferencesStore);

  /** The day the slot lands on, as yyyy-MM-dd. The board passes the day the user clicked. */
  readonly day = input<string>(zonedDay(new Date(), this.preferences.timeZone()));

  /** Where on the clock the gesture landed, as HH:mm. Ignored when the form was opened from the button. */
  readonly startAt = input<string | null>(null);

  /** How wide the gesture was, in hours. A click supplies an hour; a sweep supplies what was swept. */
  readonly durationHours = input<number | null>(null);

  /**
   * What the clicked row already said.
   *
   * The row is the fastest answer to the two questions this form is slowest at. A project row says both the type
   * and the project; a category row says only the bucket, which still removes one dropdown. Null for either means
   * the form was opened from the button and has nothing to go on.
   */
  readonly activityTypeCode = input<string | null>(null);
  readonly projectId = input<string | null>(null);

  readonly closed = output<void>();

  protected readonly types = this.activities.types;

  protected readonly kind = signal<ActivityKind>('actual');
  protected readonly typeCode = signal('');
  protected readonly project = signal<string>('');
  protected readonly startTime = signal('09:00');
  protected readonly hours = signal(1);
  protected readonly note = signal('');

  protected readonly source = signal<ActivitySource>('manual');
  protected readonly externalRef = signal<string | null>(null);

  /** A full working day, as the department defines it. */
  protected readonly fullDay = this.activities.dailyTargetHours;

  /**
   * Morning, afternoon, whole day — the department's own hours, one press each.
   *
   * Typing a start time and a duration is the slowest part of logging an hour, and almost every entry is one of
   * these three. They are read from the department rather than fixed, because 09:00 is not a universal morning:
   * a helpdesk on an early shift would otherwise have to correct the shortcut every time.
   */
  protected readonly sessions = computed(() => sessionsOf(this.activities.workingDay()));

  /**
   * Which preset the current start and duration correspond to, if any.
   *
   * Derived rather than stored, so a preset stays lit when it still describes the form and goes dark the moment
   * somebody edits the hours by hand. A stored flag would keep claiming "afternoon" over a value that is no
   * longer one.
   */
  protected readonly activeSession = computed<SessionId | null>(() => {
    const start = this.startTime();
    const hours = this.hours();

    return (
      this.sessions().find(
        (session) => session.start === start && Math.abs(this.hoursFor(session) - hours) < 0.01,
      )?.id ?? null
    );
  });

  protected readonly tasks = signal<readonly AssignableTask[]>([]);
  protected readonly tasksLoaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The chosen type's own rule, straight from the department taxonomy. */
  protected readonly requiresProject = computed(
    () => this.types().find((type) => type.code === this.typeCode())?.requiresProject ?? false,
  );

  protected readonly canSubmit = computed(
    () =>
      this.typeCode().length > 0 &&
      this.hours() > 0 &&
      // Mirrors the server's rule so the button explains itself, rather than the user pressing it to find out.
      (!this.requiresProject() || this.project().length > 0),
  );

  constructor() {
    // The gesture wins over the defaults where there was one. Opened from the button there is no gesture, and the
    // form keeps 09:00 and an hour — which is what somebody typing from scratch would have picked anyway.
    queueMicrotask(() => {
      const at = this.startAt();
      const span = this.durationHours();

      if (at) {
        this.startTime.set(at);
      }

      if (span && span > 0) {
        this.hours.set(Math.max(0.25, Math.round(span * 4) / 4));
      }

      const type = this.activityTypeCode();
      const project = this.projectId();

      if (type) {
        this.typeCode.set(type);
      }

      if (project) {
        this.project.set(project);
      }
    });
  }

  /**
   * Applies a session, or clears it.
   *
   * A second press on the active one returns to an hour at 09:00, so the button is not a trap — the same promise
   * the single "all day" button made before there were three of them.
   */
  protected toggleSession(session: DaySession): void {
    if (this.activeSession() === session.id) {
      this.startTime.set('09:00');
      this.hours.set(1);

      return;
    }

    this.startTime.set(session.start);
    this.hours.set(this.hoursFor(session));
  }

  /**
   * How long a session is, in the hours the entry records.
   *
   * The whole-day preset is capped at the department's daily target rather than being its span: a day drawn
   * 06:00-20:00 is the outer bound of when work may be recorded, not a claim that anyone works fourteen hours.
   * The two half-day presets are their real length, because those are worked end to end.
   */
  private hoursFor(session: DaySession): number {
    const span = hoursBetween(session.start, session.end);

    return session.id === 'day' ? Math.min(span, this.fullDay()) : span;
  }

  protected async loadTasks(): Promise<void> {
    this.tasks.set(await this.activities.assignableTasks());
    this.tasksLoaded.set(true);
  }

  /**
   * Applies a pulled task to the form.
   *
   * Pre-fills rather than submits. The one thing the external system cannot tell us is how long the person spent,
   * which is the entire point of the entry — so the form stays open on exactly that field.
   */
  protected pick(task: AssignableTask): void {
    this.source.set(task.source);
    this.externalRef.set(task.externalRef);
    this.note.set(task.title);

    if (task.projectId) {
      this.project.set(task.projectId);
    }

    if (task.suggestedActivityTypeCode) {
      this.typeCode.set(task.suggestedActivityTypeCode);
    }
  }

  protected clearSource(): void {
    this.source.set('manual');
    this.externalRef.set(null);
  }

  protected async submit(): Promise<void> {
    if (!this.canSubmit()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      // The day and the time are a clock reading in the person's own zone; the API takes the instant that
      // reading stands for. Building the Date from the string directly would silently mean "in whichever zone
      // this browser is set to", which is the one thing this form must not assume.
      const start = instantOf(this.day(), this.startTime(), this.preferences.timeZone());
      const end = new Date(start.getTime() + this.hours() * 3_600_000);

      await this.activities.log({
        activityTypeCode: this.typeCode(),
        projectId: this.requiresProject() ? this.project() : null,
        kind: this.kind(),
        source: this.source(),
        externalRef: this.externalRef(),
        slotStart: start.toISOString(),
        slotEnd: end.toISOString(),
        hours: this.hours(),
        note: this.note() || null,
      });

      this.closed.emit();
    } catch (failure: unknown) {
      const problem = failure as { error?: { detail?: string; title?: string } };

      // A blocked week and a rejected type both arrive here. Showing the server's own sentence is better than a
      // generic failure, because the server's sentence says which rule stopped them and what to do about it.
      this.error.set(problem.error?.detail ?? problem.error?.title ?? 'activity.genericError');
    } finally {
      this.busy.set(false);
    }
  }

  protected cancel(): void {
    this.closed.emit();
  }
}
