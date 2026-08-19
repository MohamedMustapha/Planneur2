import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  ActivitiesStore,
  ActivityKind,
  ActivitySource,
  AssignableTask,
} from '../../core/activities/activities.store';
import { ProjectsStore } from '../../core/projects/projects.store';

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

  /** The day the slot lands on, as yyyy-MM-dd. The board passes the day the user clicked. */
  readonly day = input<string>(new Date().toISOString().slice(0, 10));

  readonly closed = output<void>();

  protected readonly types = this.activities.types;

  protected readonly kind = signal<ActivityKind>('actual');
  protected readonly typeCode = signal('');
  protected readonly projectId = signal<string>('');
  protected readonly startTime = signal('09:00');
  protected readonly hours = signal(1);
  protected readonly note = signal('');

  protected readonly source = signal<ActivitySource>('manual');
  protected readonly externalRef = signal<string | null>(null);

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
      (!this.requiresProject() || this.projectId().length > 0),
  );

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
      this.projectId.set(task.projectId);
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
      const start = new Date(`${this.day()}T${this.startTime()}:00`);
      const end = new Date(start.getTime() + this.hours() * 3_600_000);

      await this.activities.log({
        activityTypeCode: this.typeCode(),
        projectId: this.requiresProject() ? this.projectId() : null,
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
