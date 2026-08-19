import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  BoardType,
  SchedulingStore,
  ShiftTemplate,
  WorkOrderView,
} from '../../core/scheduling/scheduling.store';
import { SessionStore } from '../../core/session/session.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { ProjectsStore } from '../../core/projects/projects.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { BoardTimeline, TimelineMove } from '../../shared/timeline/board-timeline/board-timeline';

/**
 * The board switcher, and the three archetypes around the shared timeline.
 *
 * One screen for all five boards. Which one is showing is a tab, not a route, because the scope selector and the
 * week pager are the same controls throughout and splitting them across five routes would mean five copies that
 * drift.
 */
@Component({
  selector: 'app-boards',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, PageHeader, BoardTimeline],
  templateUrl: './boards.html',
  styleUrl: './boards.scss',
})
export class Boards {
  protected readonly scheduling = inject(SchedulingStore);
  protected readonly session = inject(SessionStore);
  protected readonly directory = inject(DirectoryStore);
  protected readonly projects = inject(ProjectsStore);

  /**
   * Which board this route names, bound from the route's data by withComponentInputBinding.
   *
   * The rail has separate entries for "my team" and "department", and both have to land on the board they name —
   * otherwise clicking Département shows the personal board with Département merely available as a tab.
   */
  readonly board = input<BoardType>('my');

  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** The pool card the user picked up. 6a assigns by picking a card, then a person. */
  protected readonly heldOrder = signal<WorkOrderView | null>(null);

  protected readonly templates = signal<readonly ShiftTemplate[]>([]);
  protected readonly rosterPerson = signal('');
  protected readonly rosterTemplate = signal('');
  protected readonly rosterDay = signal(new Date().toISOString().slice(0, 10));

  /**
   * The boards offered.
   *
   * All five, to everybody. Which rows land inside is RLS's answer, and hiding a tab because someone probably has
   * nothing in it would be the client guessing at a decision the server already makes correctly — a member on a
   * cross-department project genuinely does have a project board.
   */
  protected readonly boards: readonly { id: BoardType; labelKey: string }[] = [
    { id: 'my', labelKey: 'boards.my' },
    { id: 'team', labelKey: 'boards.team' },
    { id: 'unit', labelKey: 'boards.unit' },
    { id: 'project', labelKey: 'boards.project' },
    { id: 'department', labelKey: 'boards.department' },
  ];

  protected readonly weekLabel = computed(() => {
    const { monday, sunday } = this.scheduling.week();

    return `${format(monday)} – ${format(sunday)}`;
  });

  protected readonly people = computed(() =>
    this.scheduling.resources().filter((row) => row.kind === 'person'),
  );

  /**
   * The project board needs to be told which project.
   *
   * Only the project board takes a scope from the user. The others derive theirs from who the caller is — your
   * unit, your department — and offering a selector for those would invite people to ask for scopes RLS will
   * simply answer with nothing.
   */
  protected readonly needsProjectScope = computed(() => this.scheduling.boardType() === 'project');

  /** True while the project board is showing but no project has been picked. */
  protected readonly awaitingProject = computed(() => this.needsProjectScope() && !this.scheduling.scopeId());

  /** Whatever went wrong: an action's refusal, or the board itself failing to load. */
  protected readonly message = computed(() => {
    const refused = this.error();

    if (refused) {
      return refused;
    }

    const failure = this.scheduling.error() as { error?: { detail?: string; title?: string } } | undefined;

    return failure ? (failure.error?.detail ?? failure.error?.title ?? 'boards.genericError') : null;
  });

  constructor() {
    // An effect rather than a constructor read: the same component instance is reused when the router moves
    // between /team and /department, so a one-time read would leave the second navigation on the first board.
    effect(() => this.scheduling.show(this.board()));

    // Shift slots load when the shift board appears, not when its select is focused. Loading on focus left the
    // control briefly empty — which reads as "this department has no shifts" for exactly as long as it takes to
    // decide the feature is broken.
    effect(() => {
      if (this.scheduling.archetype() === 'shifts' && this.templates().length === 0) {
        void this.loadTemplates();
      }
    });
  }

  protected selectProject(projectId: string): void {
    this.scheduling.show('project', projectId || null);
  }

  protected select(board: BoardType): void {
    this.error.set(null);
    this.heldOrder.set(null);
    this.scheduling.show(board);
  }

  protected hold(order: WorkOrderView): void {
    // Toggling, so a card picked up by mistake can be put back down without assigning it to anybody.
    this.heldOrder.update((held) => (held?.id === order.id ? null : order));
  }

  /** 6a: the drop. The card is held, the row is chosen, and the assignment creates the planned activity. */
  protected async dropOn(personId: string): Promise<void> {
    const order = this.heldOrder();

    if (!order) {
      return;
    }

    const start = new Date(this.scheduling.week().monday);
    start.setHours(9, 0, 0, 0);

    await this.run(async () => {
      await this.scheduling.assign(order.id, personId, start);
      this.heldOrder.set(null);
    });
  }

  protected async unassign(entryId: string): Promise<void> {
    await this.run(() => this.scheduling.unassign(entryId));
  }

  protected async refreshPool(): Promise<void> {
    const unitId = this.directory.me()?.primaryUnitId;

    if (!unitId) {
      return;
    }

    await this.run(() => this.scheduling.refreshPool(unitId).then(() => undefined));
  }

  /** 6c: a block dragged on the canvas. The server refuses anything that is not a planned slot. */
  protected async onMoved(move: TimelineMove): Promise<void> {
    await this.run(() => this.scheduling.rescheduleTask(move.eventId, move.start, move.end));
  }

  private async loadTemplates(): Promise<void> {
    this.templates.set(await this.scheduling.shiftTemplates());
  }

  protected async roster(): Promise<void> {
    if (!this.rosterPerson() || !this.rosterTemplate()) {
      return;
    }

    await this.run(() =>
      this.scheduling.planShift(this.rosterPerson(), this.rosterTemplate(), new Date(this.rosterDay())),
    );
  }

  /**
   * Runs a board action and surfaces the refusal.
   *
   * Every one of these can legitimately be refused — a blocked week, a double-booking, a row RLS will not have —
   * and the server's own sentence says which rule and what to do, so it is shown rather than replaced.
   */
  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (failure: unknown) {
      const problem = failure as { error?: { detail?: string; title?: string } };

      this.error.set(problem.error?.detail ?? problem.error?.title ?? 'boards.genericError');
    } finally {
      this.busy.set(false);
    }
  }
}

function format(date: Date): string {
  return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}`;
}
