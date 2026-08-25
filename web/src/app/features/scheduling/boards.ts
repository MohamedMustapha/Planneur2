import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { fromWallClock, zonedDay } from '../../core/time/zoned';
import {
  BoardType,
  PlanTaskRequest,
  SchedulingStore,
  ShiftTemplate,
  WorkOrderView,
} from '../../core/scheduling/scheduling.store';
import { SessionStore } from '../../core/session/session.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { ProjectsStore } from '../../core/projects/projects.store';
import { ActivitiesStore } from '../../core/activities/activities.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import {
  BoardTimeline,
  TimelineCreate,
  TimelineMove,
  TimelineProgress,
} from '../../shared/timeline/board-timeline/board-timeline';
import { KudosMonthly } from '../kudos/kudos-monthly';
import { OrgAdminStore } from '../../core/admin/org-admin.store';
import { TaskCandidate, TaskPopup, TaskSeed } from './task-popup';

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
  imports: [TranslocoDirective, FormsModule, PageHeader, BoardTimeline, KudosMonthly, TaskPopup],
  templateUrl: './boards.html',
  styleUrl: './boards.scss',
})
export class Boards {
  protected readonly scheduling = inject(SchedulingStore);
  // Only for the working day, which is department policy rather than board state: the axis this canvas draws has
  // to be the same one the personal board draws, or the same hour would sit in two places.
  protected readonly activities = inject(ActivitiesStore);
  private readonly preferences = inject(PreferencesStore);
  protected readonly session = inject(SessionStore);
  protected readonly directory = inject(DirectoryStore);
  protected readonly projects = inject(ProjectsStore);
  protected readonly org = inject(OrgAdminStore);
  private readonly transloco = inject(TranslocoService);

  /** Which board this screen opens on. One rail entry reaches it, and it is the node's. */
  readonly board = input<BoardType>('node');

  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** The pool card the user picked up. 6a assigns by picking a card, then a person. */
  protected readonly heldOrder = signal<WorkOrderView | null>(null);

  /**
   * The click on empty canvas, held while the popup asks what it means.
   *
   * Null closes the popup, which is also what a successful save sets it to — one signal rather than a separate
   * open flag, so the two can never disagree about whether the dialog is up.
   */
  protected readonly pendingTask = signal<TaskSeed | null>(null);

  /** Rows a task can be planned onto, for the case where the click landed on a header rather than a person. */
  protected readonly taskCandidates = computed<readonly TaskCandidate[]>(() =>
    this.scheduling
      .resources()
      .filter((row) => row.kind === 'person')
      .map((row) => ({ id: row.id, name: row.name })),
  );

  protected readonly templates = signal<readonly ShiftTemplate[]>([]);
  protected readonly rosterPerson = signal('');
  protected readonly rosterTemplate = signal('');
  protected readonly rosterDay = signal(zonedDay(new Date(), this.preferences.timeZone()));

  /**
   * The boards offered: my own week, the node, and an item.
   *
   * Three, not five. The node board is one board at any depth (v2 §00 §3) — what it shows is the server's answer
   * to which node it was asked about, not a different screen per rung.
   */
  protected readonly boards: readonly { id: BoardType; labelKey: string }[] = [
    { id: 'my', labelKey: 'boards.my' },
    { id: 'node', labelKey: 'boards.node' },
    { id: 'project', labelKey: 'boards.project' },
  ];

  /** Nodes the viewer may look at. RLS already narrowed the list; picking one never widens it. */
  protected readonly nodes = computed(() => this.org.nodes());

  protected readonly showsKudos = computed(() => this.scheduling.boardType() === 'node');

  protected readonly kudosScope = computed<'unit' | 'department'>(() => 'unit');

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

  protected readonly onNodeBoard = computed(() => this.scheduling.boardType() === 'node');

  /** True while the project board is showing but no project has been picked. */
  protected readonly awaitingProject = computed(
    () => this.needsProjectScope() && !this.scheduling.scopeId(),
  );

  /** Whatever went wrong: an action's refusal, or the board itself failing to load. */
  protected readonly message = computed(() => {
    const refused = this.error();

    if (refused) {
      return refused;
    }

    const failure = this.scheduling.error() as
      { error?: { detail?: string; title?: string } } | undefined;

    return failure
      ? (failure.error?.detail ?? failure.error?.title ?? 'boards.genericError')
      : null;
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

  /**
   * The overlay's kind, as a label.
   *
   * Kinds are extensible per department (S7), so an unmapped one renders as its own code rather than as a blank
   * chip — the same fallback the timeline already uses for bucket names.
   */
  protected overlayLabel(kind: string): string {
    const key = `meetings.overlayKind.${kind}`;
    const translated = this.transloco.translate(key);

    return translated === key ? kind : translated;
  }

  protected selectProject(projectId: string): void {
    this.scheduling.show('project', projectId || null);
  }

  protected selectNode(nodeId: string): void {
    this.scheduling.show('node', nodeId || null);
  }

  protected toggleExpandPeople(): void {
    this.scheduling.expandPeople.update((expanded) => !expanded);
  }

  protected select(board: BoardType): void {
    this.error.set(null);
    this.heldOrder.set(null);
    this.pendingTask.set(null);
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

    // Monday at 09:00 on the assigner's own clock, not the browser's — the same reading everyone looking at this
    // board would give the slot they just dropped a work order into.
    const monday = new Date(this.scheduling.week().monday);
    monday.setHours(9, 0, 0, 0);

    const start = fromWallClock(monday, this.preferences.timeZone());

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

  /**
   * 6c: a click, or a sweep, on empty canvas.
   *
   * The row id is decoded rather than passed through, because a project line's id is a person and a project glued
   * together — which means a click on that line has already answered two of the popup's questions and should not
   * ask them again.
   */
  protected onCreate(request: TimelineCreate): void {
    const row = this.scheduling
      .resources()
      .find((candidate) => candidate.id === request.resourceId);
    const [rowPersonId, rowProjectId] = request.resourceId.split(':');
    const me = this.directory.me();

    // Three row shapes reach here, and each answers a different amount of the form. A person row names who; a
    // project line names who and against what; a lane on the personal board names the caller and which bucket.
    const person =
      row?.kind === 'person'
        ? { id: row.id, name: row.name }
        : row?.kind === 'project-line'
          ? { id: rowPersonId, name: this.personName(rowPersonId) }
          : row?.kind === 'lane' && me
            ? { id: me.personId, name: me.displayName }
            : null;

    this.error.set(null);
    this.pendingTask.set({
      personId: person?.id ?? null,
      personName: person?.name ?? null,
      projectId: row?.kind === 'project-line' ? (rowProjectId ?? null) : null,
      nature: row?.kind === 'lane' ? natureOf(row.id) : null,
      start: request.start,
      end: request.end,
    });
  }

  protected async createTask(request: PlanTaskRequest): Promise<void> {
    await this.run(async () => {
      await this.scheduling.planTask(request);
      // Only on success. A refusal leaves the popup open on what the user typed, because retyping four fields to
      // find out the second refusal is the same as the first is how people stop using a dialog.
      this.pendingTask.set(null);
    });
  }

  /** 6c: the progress handle, released. */
  protected async onProgressed(change: TimelineProgress): Promise<void> {
    await this.run(() => this.scheduling.setTaskProgress(change.eventId, change.percentComplete));
  }

  /** 6c: a block dragged on the canvas. The server refuses anything that is not a planned slot. */
  protected async onMoved(move: TimelineMove): Promise<void> {
    await this.run(() => this.scheduling.rescheduleTask(move.eventId, move.start, move.end));
  }

  /** A person row's name, for the popup's header. Falls back to nothing rather than to an id. */
  private personName(personId: string): string | null {
    return this.scheduling.resources().find((row) => row.id === personId)?.name ?? null;
  }

  private async loadTemplates(): Promise<void> {
    this.templates.set(await this.scheduling.shiftTemplates());
  }

  protected async roster(): Promise<void> {
    if (!this.rosterPerson() || !this.rosterTemplate()) {
      return;
    }

    await this.run(() =>
      this.scheduling.planShift(
        this.rosterPerson(),
        this.rosterTemplate(),
        new Date(this.rosterDay()),
      ),
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

/**
 * The BUILD or RUN answer a lane already carries.
 *
 * Only the two canonical buckets, and their subtypes by prefix. A quality-of-life lane maps to neither, and
 * guessing BUILD for it would put non-project hours against a project the moment the user pressed save.
 */
function natureOf(laneCode: string): 'build' | 'run' | null {
  return laneCode.startsWith('project-build')
    ? 'build'
    : laneCode.startsWith('project-run')
      ? 'run'
      : null;
}

function format(date: Date): string {
  return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}`;
}
