import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ActivityEntryView } from '../../core/activities/activities.store';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { SessionStore } from '../../core/session/session.store';
import { FocusStore } from '../../core/focus/focus.store';
import { GuidanceBanner } from '../../shared/ui/guidance-banner/guidance-banner';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ScopeSelector } from '../../shared/ui/scope-selector/scope-selector';
import { LayoutStore } from '../../core/layout/layout.store';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { fromWallClock, toWallClock, zonedDay, zonedTime } from '../../core/time/zoned';
import { SchedulingStore } from '../../core/scheduling/scheduling.store';
import {
  BoardTimeline,
  TimelineCreate,
  TimelineMove,
  TimelineProgress,
} from '../../shared/timeline/board-timeline/board-timeline';
import { ActivitiesStore } from '../../core/activities/activities.store';
import { QuickAdd } from './quick-add';
import { UpcomingStrip } from '../meetings/upcoming-strip';

type BoardTab = 'week' | 'month' | 'list';

/** 2πr for the r=36 ring the header draws. A constant because the geometry is fixed by the design, not by data. */
const RING_CIRCUMFERENCE = 226;

/**
 * "Mon tableau" — the daily activity-logging screen, and the shell's reference page.
 *
 * S0 built the frame and left every number reading zero, because showing invented figures in a tool whose whole
 * purpose is accurate time reporting would have been exactly the wrong kind of placeholder. S5 fills it in: the
 * meter, the tiles and the day list are now this person's real week, and quick add writes to it.
 *
 * The canvas is S6's real timeline rather than S0's empty proof, and it is the fast path: clicking an empty slot
 * opens the log form with the day, the time and the duration already answered. That matters more here than on any
 * other screen — this is the one people are asked to visit every day, and every field they have to re-type is a
 * reason not to. The day list underneath stays, because "what did I book on Tuesday" is a reading question and a
 * list answers it better than a grid.
 *
 * v2 re-shells it against `02-navigation-focus-mode.md`, and most of that work was deletion. §3 names this screen
 * specifically: the right rail is gone — "À venir" is now a slim strip above the grid, the keyboard-hint block has
 * become a `?` popover, and the "Mon unité" placeholder simply stopped existing. What is left is a title, a
 * sentence saying what the page is for, one meter, one primary action and the grid. In Focus mode it is the grid,
 * the meter and Quick-add, and nothing else at all.
 */
@Component({
  selector: 'app-board',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslocoDirective,
    PageHeader,
    GuidanceBanner,
    ScopeSelector,
    BoardTimeline,
    QuickAdd,
    UpcomingStrip,
  ],
  templateUrl: './board.html',
  styleUrl: './board.scss',
  // On the document rather than the host: the rail advertises these keys as working anywhere on the screen, and a
  // host listener only fires once something inside the component has focus.
  host: { '(document:keydown)': 'onKeydown($event)' },
})
export class Board {
  protected readonly session = inject(SessionStore);
  protected readonly departments = inject(DepartmentScopeStore);
  protected readonly directory = inject(DirectoryStore);

  protected readonly activities = inject(ActivitiesStore);
  protected readonly scheduling = inject(SchedulingStore);
  protected readonly focus = inject(FocusStore);
  private readonly layout = inject(LayoutStore);
  private readonly preferences = inject(PreferencesStore);

  protected readonly activeTab = signal<BoardTab>('week');

  protected readonly quickAddOpen = signal(false);

  /**
   * Where on the calendar the quick-add was opened from, or null when it was opened from the button.
   *
   * Held rather than passed straight through because the form is a child that only exists while it is open: the
   * gesture happens first and has to survive until the component is created.
   */
  protected readonly quickAddSeed = signal<{
    day: string;
    startAt: string;
    hours: number;
    activityTypeCode: string | null;
    projectId: string | null;
  } | null>(null);

  /**
   * The shortcut list, moved out of the old right rail into a popover behind the `?` control (§02.3).
   *
   * The hints were taking a permanent quarter of the screen to teach two keystrokes, which is a bad trade after
   * the first week and a terrible one after the first month. They are still discoverable, just not resident.
   */
  protected readonly shortcutsOpen = signal(false);

  /** What the last shortcut did, or why it did nothing. Cleared as soon as the next one runs. */
  protected readonly notice = signal<string | null>(null);

  protected readonly busy = signal(false);

  protected readonly tabs: readonly { id: BoardTab; labelKey: string }[] = [
    { id: 'week', labelKey: 'board.tabs.week' },
    { id: 'month', labelKey: 'board.tabs.month' },
    { id: 'list', labelKey: 'board.tabs.list' },
  ];

  /** From the department's own configuration, defaulting to the statutory 35 where it has said nothing. */
  protected readonly weeklyTargetHours = this.activities.targetHours;

  protected readonly loggedHours = this.activities.loggedHours;

  protected readonly remainingHours = computed(() =>
    Math.max(0, this.weeklyTargetHours() - this.loggedHours()),
  );

  protected readonly meterPercent = computed(() =>
    Math.min(100, (this.loggedHours() / this.weeklyTargetHours()) * 100),
  );

  /**
   * The ring's dash offset, for the donut the full-mode header draws.
   *
   * A ring rather than a bar in full mode because the header has square-ish room and a percentage read at a glance
   * is what the meter is for; Focus mode keeps the bar, which is the shape that survives being the only element
   * on a line. Both are driven from the same two numbers, so they can never disagree.
   */
  protected readonly meterOffset = computed(
    () => RING_CIRCUMFERENCE * (1 - Math.min(1, this.loggedHours() / this.weeklyTargetHours())),
  );

  protected readonly ringCircumference = RING_CIRCUMFERENCE;

  /**
   * Blue while filling, green once the week is essentially complete, amber past the target. The amber state is a
   * soft warning by default; a department that enforces the cap turns it into a block in S5.
   */
  protected readonly meterColor = computed(() => {
    const logged = this.loggedHours();
    const target = this.weeklyTargetHours();

    // Amber past the target whether the department warns or blocks. A blocking department simply never lets the
    // number get here through the form — and when a lead records overtime on someone's behalf, it still should.
    if (logged > target) {
      return 'var(--warning)';
    }

    return logged >= target - 2 ? 'var(--success)' : 'var(--primary)';
  });

  /**
   * The lane legend above the grid: one chip per canonical bucket, carrying the hours that bucket accounts for.
   *
   * Canonical rather than every type the department offers, for the same reason the tiles are — a department with
   * fifteen subtypes would wrap the legend onto three lines and stop being a legend.
   */
  protected readonly lanes = computed(() =>
    [
      // `id` is the chip's colour class, `code` the taxonomy bucket. They differ because the theme names colours
      // after what they mean and the taxonomy names codes after what they are, and collapsing the two would tie
      // the palette to the seed data.
      { id: 'build', code: 'project-build' },
      { id: 'run', code: 'project-run' },
      { id: 'qol', code: 'quality-of-life' },
      { id: 'admin', code: 'recruitment-admin' },
    ].map((lane) => ({
      ...lane,
      labelKey: `activity.type.${lane.code}`,
      hours: this.activities.hoursFor(lane.code),
    })),
  );

  /**
   * "Camille Villeneuve · Infrastructure & Réseaux" — name, unit, department, whichever of them the directory
   * knows. Built from the directory rather than the token now that S1 supplies it, so the unit appears too.
   */
  protected readonly subtitle = computed(() => {
    const me = this.directory.me();
    const unit = me?.units.find((candidate) => candidate.id === me.primaryUnitId);

    return [this.directory.displayName(), unit?.name].filter(Boolean).join(' · ');
  });

  /**
   * The four tiles, against the canonical buckets.
   *
   * Canonical rather than every type the department offers: the tiles are a fixed row in the design, and a
   * department with fifteen subtypes would otherwise push them off the screen. Subtype hours roll into their
   * parent bucket, which is what the parent is for.
   */
  protected readonly summaryTiles = computed(() => [
    {
      id: 'build',
      labelKey: 'board.summary.build',
      color: 'var(--activity-build)',
      hours: this.activities.hoursFor('project-build'),
    },
    {
      id: 'run',
      labelKey: 'board.summary.run',
      color: 'var(--activity-run)',
      hours: this.activities.hoursFor('project-run'),
    },
    {
      id: 'qol',
      labelKey: 'board.summary.qolTraining',
      color: 'var(--activity-qol)',
      hours: this.activities.hoursFor('quality-of-life'),
    },
    {
      // Planned against actual — the gap the module exists to make visible. Signed, because "we planned more than
      // we did" and "we did more than we planned" are different stories and an absolute value tells neither.
      id: 'variance',
      labelKey: 'board.summary.variance',
      color: 'var(--ink)',
      hours: this.activities.loggedHours() - this.activities.plannedHours(),
    },
  ]);

  /** The tiles Focus mode keeps: none. Kept as a computed so the template asks one question rather than four. */
  protected readonly showSecondaryPanels = computed(() => !this.focus.active());

  constructor() {
    // The personal board, and this screen's own pager drives it. The shell's week offset is documented as shared
    // state for exactly this reason — two pagers on one screen disagreeing about which week it is would be worse
    // than either of them being wrong.
    effect(() => {
      this.scheduling.show('my');
      this.scheduling.weekOffset.set(this.layout.weekOffset());
    });
  }

  /** The week's entries grouped by day, in the order the days fall. */
  protected readonly days = computed(() => {
    const groups = new Map<string, { day: string; entries: ActivityEntryView[] }>();

    const zone = this.preferences.timeZone();

    for (const entry of this.activities.entries()) {
      // Grouped on the person's own calendar day rather than on the first ten characters of the stored instant,
      // which is a UTC slice: an entry logged at 19:00 in Paris carries a slotStart of 17:00Z in summer and lands
      // on the right day by luck, while one logged at 01:00 lands on the day before.
      const day = zonedDay(entry.slotStart, zone);

      (groups.get(day) ?? groups.set(day, { day, entries: [] }).get(day)!).entries.push(entry);
    }

    return [...groups.values()].sort((left, right) => left.day.localeCompare(right.day));
  });

  /**
   * Opens the form on the slot that was clicked.
   *
   * The whole point of the gesture: pointing at Tuesday afternoon has already said the day, the time and roughly
   * how long, and asking for all three again in a form is why people stop logging their week. What it cannot say
   * is which activity type — that is the department's taxonomy and the one field nobody can guess — so the form
   * still opens, just three answers ahead.
   */
  protected onTimelineCreate(request: TimelineCreate): void {
    const start = request.start;

    this.quickAddSeed.set({
      // The gesture arrives as an instant; the form collects a day and a time on the person's own clock. Reading
      // it any other way books an evening entry on the wrong date for half the world.
      day: zonedDay(start, this.preferences.timeZone()),
      startAt: zonedTime(start, this.preferences.timeZone()),
      hours: Math.max(0.25, (request.end.getTime() - start.getTime()) / 3_600_000),
      // A category row names a bucket, which is a real activity type in its own right — so it seeds the type
      // field just as a subtype row does, and the person changes it only if they meant something more specific.
      activityTypeCode: request.activityTypeCode ?? request.categoryCode,
      projectId: request.projectId,
    });

    this.quickAddOpen.set(true);
  }

  /** A block dragged to another time. Planned slots only — the server refuses to move an actual. */
  protected async onTimelineMove(move: TimelineMove): Promise<void> {
    await this.scheduling.rescheduleTask(move.eventId, move.start, move.end);
    this.activities.reload();
  }

  protected async onTimelineProgress(change: TimelineProgress): Promise<void> {
    await this.scheduling.setTaskProgress(change.eventId, change.percentComplete);
    this.activities.reload();
  }

  /**
   * The shortcuts the rail has been advertising.
   *
   * Ignored while the caret is in a field, so typing "n" into the note of the very form this opens does not open
   * a second one. Modifier combinations are left alone too — ctrl+N is the browser's, not ours.
   */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.ctrlKey || event.metaKey || event.altKey || isTyping(event.target)) {
      return;
    }

    const key = event.key.toLowerCase();

    if (event.key === 'Escape') {
      this.shortcutsOpen.set(false);

      return;
    }

    if (key === 'n' && !this.quickAddOpen()) {
      event.preventDefault();
      this.openQuickAdd();

      return;
    }

    if (key === 'd' && !this.quickAddOpen()) {
      event.preventDefault();
      void this.duplicateYesterday();
    }
  }

  /**
   * Copies the previous working day's actuals onto today.
   *
   * The whole point of the feature for anyone on one project all week: yesterday's row is today's row, and
   * re-entering it by hand four times a week is the reason timesheets go unfilled. Only actuals are copied — a
   * plan is a statement about the future and duplicating it forward would invent one nobody made.
   *
   * Previous *working* day, so Monday reaches back to Friday rather than to an empty Sunday. Friday may fall
   * outside the week currently loaded, which is the one case this cannot serve from the feed it already has; it
   * says so rather than silently copying nothing.
   */
  protected async duplicateYesterday(): Promise<void> {
    this.notice.set(null);

    const zone = this.preferences.timeZone();
    const today = new Date();
    const source = previousWorkingDay(toWallClock(today, zone));
    const sourceDay = localDay(source);

    const entries = this.activities
      .entries()
      .filter((entry) => entry.kind === 'actual' && zonedDay(entry.slotStart, zone) === sourceDay);

    if (entries.length === 0) {
      this.notice.set('board.nothingToDuplicate');

      return;
    }

    this.busy.set(true);

    try {
      // Sequential rather than in parallel: every write re-runs the weekly guardrail against the running total,
      // and firing them together would let a batch that should have been refused past the cap slip through.
      for (const entry of entries) {
        // Same time of day, on today — where "time of day" means what this person's clock read, not what the
        // browser's did.
        const start = fromWallClock(
          shiftToDay(toWallClock(entry.slotStart, zone), toWallClock(today, zone)),
          zone,
        );
        const end = new Date(start.getTime() + entry.hours * 3_600_000);

        await this.activities.log({
          activityTypeCode: entry.activityTypeCode,
          projectId: entry.projectId,
          kind: 'actual',
          // Deliberately not the original's source or external reference. A copy is something this person did
          // today by hand; claiming it came from ServiceNow would make the audit trail back to the ticket a lie.
          slotStart: start.toISOString(),
          slotEnd: end.toISOString(),
          hours: entry.hours,
          note: entry.note,
        });
      }

      this.scheduling.reload();
      this.notice.set('board.duplicated');
    } catch (failure: unknown) {
      const problem = failure as { error?: { detail?: string; title?: string } };

      this.notice.set(problem.error?.detail ?? problem.error?.title ?? 'board.duplicateFailed');
    } finally {
      this.busy.set(false);
    }
  }

  protected selectTab(tab: BoardTab): void {
    this.activeTab.set(tab);
  }

  protected toggleShortcuts(): void {
    this.shortcutsOpen.update((open) => !open);
  }

  protected openQuickAdd(): void {
    // No seed: opened from the button, the form keeps its own sensible defaults rather than the last gesture's.
    this.quickAddSeed.set(null);
    this.quickAddOpen.set(true);
  }

  protected closeQuickAdd(): void {
    this.quickAddOpen.set(false);
    this.quickAddSeed.set(null);
    // The canvas is drawn from the composed board, which does not know an entry was written through S5's form.
    this.scheduling.reload();
  }

  protected async remove(entryId: string): Promise<void> {
    await this.activities.remove(entryId);
    this.scheduling.reload();
  }
}

/**
 * A wall-clock date as `yyyy-MM-dd`.
 *
 * Not toISOString().slice(0, 10): that converts to UTC first, so a Monday evening comes back as Tuesday for half
 * the year. Takes a Date whose local fields are already the intended clock face — {@link zonedDay} is the version
 * that starts from an instant.
 */
function localDay(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

/**
 * True when the key belongs to whatever the user is typing in.
 *
 * `isContentEditable` as well as the tag names: the note field today is an input, but a rich-text one later would
 * be a div, and a shortcut that started eating keystrokes at that point would be a puzzling bug to trace.
 */
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  return target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);
}

/** Monday reaches back to Friday; every other day reaches back one. */
function previousWorkingDay(from: Date): Date {
  const previous = new Date(from);

  previous.setDate(previous.getDate() - (from.getDay() === 1 ? 3 : 1));

  return previous;
}

/** The same time of day, on another date. */
function shiftToDay(source: Date, day: Date): Date {
  const moved = new Date(day);

  moved.setHours(source.getHours(), source.getMinutes(), 0, 0);

  return moved;
}
