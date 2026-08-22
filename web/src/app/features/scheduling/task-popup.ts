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
import { ActivitiesStore } from '../../core/activities/activities.store';
import { ProjectsStore } from '../../core/projects/projects.store';
import { PlanTaskRequest } from '../../core/scheduling/scheduling.store';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { zonedDay, zonedTime } from '../../core/time/zoned';

/** Whose row was clicked, and which hours were swept. Everything else the popup asks for. */
export interface TaskSeed {
  readonly personId: string | null;
  readonly personName: string | null;
  readonly projectId: string | null;
  /** Prefilled where the row itself named a bucket — a BUILD lane on the personal board. */
  readonly nature: 'build' | 'run' | null;
  readonly start: Date;
  readonly end: Date;
}

export interface TaskCandidate {
  readonly id: string;
  readonly name: string;
}

/** The canonical buckets the BUILD/RUN choice maps onto. Departments relabel these; they cannot redefine them. */
const BUILD_CODE = 'project-build';
const RUN_CODE = 'project-run';

/**
 * The create popup behind a click on the timeline.
 *
 * Four questions, because four is what the server cannot answer for itself: which project, BUILD or RUN, what the
 * work is in a sentence, and how far along it already is. Whose row and which hours came from the gesture and are
 * shown rather than asked — re-asking the user for what they just pointed at is how a popup becomes a form.
 *
 * BUILD and RUN are a segmented pair rather than the full type dropdown that S5's quick-add offers. The board is
 * for project work; recruitment and quality-of-life hours are real but they are not tasks with a percentage, and
 * offering them here would produce blocks the progress bar cannot describe.
 */
@Component({
  selector: 'app-task-popup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule],
  templateUrl: './task-popup.html',
  styleUrl: './task-popup.scss',
})
export class TaskPopup {
  private readonly activities = inject(ActivitiesStore);
  protected readonly projects = inject(ProjectsStore);

  readonly seed = input.required<TaskSeed>();

  /** Offered only when the clicked row did not name a person — a unit or department header. */
  readonly people = input<readonly TaskCandidate[]>([]);

  /** The server's refusal, passed back down so the popup stays open on the field that caused it. */
  readonly error = input<string | null>(null);
  readonly busy = input(false);

  readonly submitted = output<PlanTaskRequest>();
  readonly cancelled = output<void>();

  protected readonly nature = signal<'build' | 'run'>('build');
  protected readonly projectId = signal('');
  protected readonly description = signal('');
  protected readonly percentComplete = signal(0);
  protected readonly personId = signal('');
  protected readonly hours = signal(1);

  /** The chosen bucket's own label, where the department relabelled it, and the canonical key otherwise. */
  protected readonly buildLabelKey = computed(() => this.labelKeyFor(BUILD_CODE));
  protected readonly runLabelKey = computed(() => this.labelKeyFor(RUN_CODE));

  protected readonly resolvedPersonId = computed(() => this.seed().personId ?? this.personId());

  /** A full working day, as the department defines it. */
  protected readonly fullDay = this.activities.dailyTargetHours;

  protected readonly isFullDay = computed(() => this.hours() === this.fullDay());

  protected readonly startLabel = computed(() => {
    // The clock face the person keeps, not the browser's. The gesture that produced this seed was made against an
    // axis drawn in their zone, so echoing it back in another one would contradict what they just pointed at.
    const zone = this.preferences.timeZone();

    return `${zonedDay(this.seed().start, zone)} ${zonedTime(this.seed().start, zone)}`;
  });

  private readonly preferences = inject(PreferencesStore);

  protected readonly canSubmit = computed(
    () => this.resolvedPersonId().length > 0 && this.projectId().length > 0 && this.hours() > 0,
  );

  constructor() {
    // The gesture's own width becomes the default length, rounded to the quarter hour the slot rules use. A click
    // on a cell arrives as one hour; a sweep arrives as whatever was swept, and overriding that would throw away
    // the more precise of the two answers.
    queueMicrotask(() => {
      const { start, end, projectId, nature } = this.seed();
      const span = (end.getTime() - start.getTime()) / 3_600_000;

      this.hours.set(Math.max(0.25, Math.round(span * 4) / 4));

      if (projectId) {
        this.projectId.set(projectId);
      }

      if (nature) {
        this.nature.set(nature);
      }
    });
  }

  /**
   * Fills the whole day in one press.
   *
   * The single most common duration there is, and the one that costs the most to type: somebody who spent the day
   * on one task has to reach for the number field, clear it, and type a figure they should not have to know. A
   * second press puts it back, so the button is not a trap.
   */
  protected toggleFullDay(): void {
    this.hours.update((current) => (current === this.fullDay() ? 1 : this.fullDay()));
  }

  protected submit(): void {
    if (!this.canSubmit()) {
      return;
    }

    const start = this.seed().start;
    const end = new Date(start.getTime() + this.hours() * 3_600_000);

    this.submitted.emit({
      personId: this.resolvedPersonId(),
      activityTypeCode: this.nature() === 'build' ? BUILD_CODE : RUN_CODE,
      projectId: this.projectId(),
      start: start.toISOString(),
      end: end.toISOString(),
      note: this.description().trim() || null,
      // Zero is a real answer — "started, nothing done" — so it is sent rather than treated as unset. Only a task
      // nobody was asked about should fall back to plan-versus-actual.
      percentComplete: this.percentComplete(),
    });
  }

  private labelKeyFor(code: string): string {
    return (
      this.activities.types().find((type) => type.code === code)?.labelKey ??
      `activity.type.${code}`
    );
  }
}
