import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import {
  MbscCalendarEvent,
  MbscEventcalendarOptions,
  MbscEventCreateEvent,
  MbscEventUpdateEvent,
  MbscModule,
  MbscResource,
} from '@mobiscroll/angular';
import { TranslocoService } from '@jsverse/transloco';
import { LanguageStore } from '../../../core/i18n/language.store';
import { PreferencesStore } from '../../../core/preferences/preferences.store';
import { fromWallClock, toWallClock } from '../../../core/time/zoned';
import {
  DEFAULT_WORKING_DAY,
  WorkingDay as TimelineWorkingDay,
  minutesOf,
} from '../../../core/time/working-day';
import {
  BoardArchetype,
  BoardEvent,
  BoardOverlay,
  BoardResource,
} from '../../../core/scheduling/scheduling.store';

/** A block the user dragged, in the shape the caller needs to persist it. */
export interface TimelineMove {
  readonly eventId: string;
  readonly resourceId: string;
  readonly start: Date;
  readonly end: Date;
}

/**
 * A gesture on empty canvas, offered as a request rather than as a fact.
 *
 * The canvas knows whose row was clicked and which hours were swept; it does not know which project the work is
 * against or whether it is BUILD or RUN, and those are not defaultable. So the gesture is refused as a creation
 * and re-offered here, for the caller to put behind a form.
 */
export interface TimelineCreate {
  readonly resourceId: string;
  readonly start: Date;
  readonly end: Date;
  /**
   * What the clicked row means, where it means anything.
   *
   * The row id alone is not enough for the caller to act on: it is a composite this component assembled, and
   * making the board parse it back apart would put the format in two places. So the parts travel as parts —
   * the bucket always, and whichever of project or activity type the row was actually about.
   */
  readonly categoryCode: string | null;
  readonly projectId: string | null;
  readonly activityTypeCode: string | null;
}

/** The progress handle, released. */
export interface TimelineProgress {
  readonly eventId: string;
  readonly percentComplete: number;
}

/**
 * The rotating timeline, wrapped once.
 *
 * All three archetypes render through this component. What differs between them is configuration — whether rows
 * accept drops, whether events show a progress bar, how wide a time step is — not markup, so there is one canvas
 * and three configurations rather than three timelines that drift apart.
 *
 * The payload arrives already shaped as resources and events; this component's whole job is translating that into
 * Mobiscroll's vocabulary and translating gestures back out.
 */
@Component({
  selector: 'app-board-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MbscModule],
  templateUrl: './board-timeline.html',
  styleUrl: './board-timeline.scss',
})
export class BoardTimeline {
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageStore);

  /**
   * The zone the canvas is drawn in.
   *
   * Mobiscroll lays events out by a Date's local fields, so every instant is converted to the person's wall clock
   * on the way in and back to a real instant on the way out. Without that the same board reads differently in
   * Paris and in Madrid — and worse, an entry dragged in one would be saved at the hour it appeared in the other.
   */
  private readonly preferences = inject(PreferencesStore);

  readonly archetype = input.required<BoardArchetype>();
  readonly resources = input.required<readonly BoardResource[]>();
  readonly events = input.required<readonly BoardEvent[]>();
  readonly overlays = input<readonly BoardOverlay[]>([]);
  readonly from = input.required<string>();
  readonly to = input.required<string>();

  /** False puts the whole canvas in read-only mode, whatever individual events say. */
  readonly editable = input(false);

  /**
   * The hours the canvas draws, as the department defines them.
   *
   * A day is not 24 hours of equal interest. Drawing midnight to midnight spends most of the width on hours
   * nobody works and squeezes the ones they do, and it invites a drag that lands at 03:00 — a time no timesheet
   * means. Bounding the axis makes the useful part of the day the whole picture, and makes the bound something
   * the user never has to be told about.
   */
  readonly workingDay = input<TimelineWorkingDay>(DEFAULT_WORKING_DAY);

  readonly moved = output<TimelineMove>();
  readonly created = output<TimelineCreate>();
  readonly progressed = output<TimelineProgress>();

  private readonly changes = inject(ChangeDetectorRef);

  /**
   * Whether events carry a progress bar.
   *
   * 6c only. A shift is not a thing that is 40% done, and a work order's progress lives in the system it was
   * pulled from — putting a handle on either would invite a number nothing downstream would read.
   */
  protected readonly showsProgress = computed(() => this.archetype() === 'task-progress');

  /**
   * Rows, nested where the payload nests them.
   *
   * Mobiscroll expresses hierarchy through a `children` array, so the flat parent-id list the server sends is
   * rebuilt into a tree here. Server-side it is flat because that is what survives JSON cleanly and what every
   * other consumer wants; only this component needs the tree.
   */
  protected readonly mbscResources = computed<MbscResource[]>(() => {
    const rows = this.resources();
    const children = new Map<string, MbscResource[]>();

    for (const row of rows.filter((candidate) => candidate.parentId)) {
      const siblings = children.get(row.parentId!) ?? [];
      siblings.push(this.toResource(row));
      children.set(row.parentId!, siblings);
    }

    return rows
      .filter((row) => !row.parentId)
      .map((row) => {
        const nested = children.get(row.id);

        return nested ? { ...this.toResource(row), children: nested } : this.toResource(row);
      });
  });

  protected readonly mbscEvents = computed<MbscCalendarEvent[]>(() =>
    this.events().map((event) => ({
      id: event.id,
      resource: event.resourceId,
      // Titles arrive as translation keys, because what a bucket is called is the department's business and the
      // server has no idea which language this browser is in.
      title: this.label(event.title),
      start: toWallClock(event.start, this.preferences.timeZone()),
      end: toWallClock(event.end, this.preferences.timeZone()),
      color: event.color ?? undefined,
      cssClass: event.cssClass ?? undefined,
      // Two gates, and both must open: the board's own read-only flag and the event's. A department board is
      // read-only entirely; a personal board is editable but its actuals are not.
      editable: this.editable() && event.editable,
      dragBetweenResources: this.archetype() === 'work-orders' && this.editable() && event.editable,
      progress: event.progress ?? undefined,
      // Kept so the event template can show the sentence somebody typed into the popup rather than only the
      // bucket's name, which every task in the row shares.
      note: event.note ?? undefined,
    })),
  );

  /**
   * Overlays as coloured ranges behind the events.
   *
   * Mobiscroll's `colors` option rather than its `invalid` one: an iteration boundary or a patch party is context,
   * not a restriction, and marking those days invalid would stop anyone scheduling on them.
   */
  protected readonly mbscColors = computed(() =>
    this.overlays().map((overlay) => ({
      // Overlays are calendar days rather than instants — an iteration runs "the 3rd to the 14th", not from one
      // moment to another — so these are already wall-clock and need no conversion.
      start: new Date(overlay.from),
      end: new Date(`${overlay.to}T23:59:59`),
      background: overlay.color ?? 'var(--surface-2)',
      title: overlay.title,
    })),
  );

  protected readonly options = computed<MbscEventcalendarOptions>(() => ({
    // No theme, themeVariant or locale here: MobiscrollOptions sets all three globally from the shell's own
    // stores. Repeating them per component is how one calendar ends up in a different language from the popup
    // that opens on top of it.
    // 6c creates from the canvas: click a cell for an hour, or sweep to say how long. Both are refused as
    // creations and re-offered as a request, because a block cannot exist without a project and a BUILD/RUN
    // answer and the canvas has neither.
    clickToCreate: this.showsProgress() && this.editable() ? 'single' : false,
    dragToCreate: this.showsProgress() && this.editable(),
    dragToMove: this.editable(),
    dragToResize: this.editable(),
    // Dropping onto the canvas from outside is how 6a's pool works: the card is not an event yet, so it cannot be
    // dragged between resources — it has to be received.
    externalDrop: this.archetype() === 'work-orders' && this.editable(),
    externalDrag: false,
    view: {
      timeline: {
        type: 'week',
        startDay: 1,
        endDay: 5,
        // Weekday columns, not hours. An hourly axis over a Monday-to-Friday week is sixty-five columns wide, so
        // the canvas opened on Monday morning and everything else was behind a horizontal scrollbar — on boards
        // whose entire question is "how is the week shaped". A day resolution answers that in one screen, and the
        // hours are still on every block: a task's own bar says how long it is, and a shift's slot name says
        // which part of the day it covers.
        resolutionHorizontal: 'day',
        // The department's own hours. Everything outside them is not drawn, which is what makes the clamp below
        // invisible: there is nowhere on the canvas that maps to 03:00 in the first place.
        startTime: this.workingDay().dayStart,
        endTime: this.workingDay().dayEnd,
        // Variable throughout, now that a day cell can hold several blocks. Fixed rows would clip the second
        // entry on any day somebody logged twice, which on a time-reporting tool is most of them.
        rowHeight: 'variable',
        eventList: false,
      },
    },
  }));

  /**
   * The week the canvas opens on, falling back to today.
   *
   * The fallback is not theoretical: a screen that renders the timeline while its board is still loading has no
   * `from` yet, and `new Date('')` is an Invalid Date that Mobiscroll cannot lay a week out around.
   */
  protected readonly selectedDate = computed(() => {
    const from = new Date(this.from());

    return Number.isNaN(from.getTime()) ? new Date() : from;
  });

  protected onEventUpdate(args: MbscEventUpdateEvent): boolean {
    const event = args.event;

    if (!event?.id || !event.start || !event.end) {
      return false;
    }

    // Clamped like a creation, and for the same reason: a block dragged past the edge of the drawn day would
    // otherwise be saved at an hour the canvas cannot show it at, and would appear to vanish.
    const span = this.withinWorkingDay(
      new Date(event.start as string | Date),
      new Date(event.end as string | Date),
    );

    this.moved.emit({
      eventId: String(event.id),
      resourceId: String(args.resource ?? event.resource ?? ''),
      ...this.asInstants(span),
    });

    return true;
  }

  /**
   * Refuses in-place creation, and asks for the form instead.
   *
   * Still false, deliberately: a block written straight onto the canvas would carry no project, no BUILD or RUN
   * answer and no description, and the server would refuse it a moment later anyway. What the gesture is good for
   * is the two things it does know — whose row, and which hours — so those are emitted and the popup fills in the
   * rest.
   */
  protected onEventCreate(args: MbscEventCreateEvent): boolean {
    const event = args.event;

    if (!this.showsProgress() || !this.editable() || !event?.start || !event.end) {
      return false;
    }

    // A created event's resource arrives as the single row it was drawn on, but the field is typed as one-or-many
    // because a saved event may span several; the first is the row that was clicked either way.
    const resource = Array.isArray(event.resource) ? event.resource[0] : event.resource;
    const resourceId = String(resource ?? '');
    const row = this.resources().find((candidate) => candidate.id === resourceId);

    const span = this.withinWorkingDay(
      new Date(event.start as string | Date),
      new Date(event.end as string | Date),
    );

    this.created.emit({
      resourceId,
      ...this.asInstants(span),
      // The row already answers what the form would otherwise have to ask. A category row answers only the
      // bucket; a project or subtype row answers the specific one too.
      categoryCode: row ? (row.parentId ?? row.id) : null,
      projectId: row?.kind === 'project-line' ? row.id.split(':')[1] ?? null : null,
      activityTypeCode: this.typeCodeOf(row),
    });

    return false;
  }

  /**
   * A wall-clock span, back as the instants it stands for.
   *
   * The last step before a gesture leaves this component: everything above works in the reader's clock face,
   * everything outside works in real time, and this is the single line between the two.
   */
  private asInstants(span: { start: Date; end: Date }): { start: Date; end: Date } {
    const zone = this.preferences.timeZone();

    return { start: fromWallClock(span.start, zone), end: fromWallClock(span.end, zone) };
  }

  /**
   * A gesture, pulled inside the department's day.
   *
   * The axis already hides the hours outside it, but a day-resolution column is a whole calendar day underneath —
   * so a click still arrives as midnight-to-midnight and has to be told what part of that day it meant. A sweep
   * that overlaps the window keeps what it overlapped; a gesture that covers the day whole is treated as "this
   * day", and the caller's own preset decides which part of it, rather than proposing a fourteen-hour entry.
   */
  private withinWorkingDay(start: Date, end: Date): { start: Date; end: Date } {
    const day = this.workingDay();
    const openAt = minutesOf(day.dayStart);
    const closeAt = minutesOf(day.dayEnd);

    const clamp = (value: Date, minutes: number): Date => {
      const moved = new Date(value);
      moved.setHours(Math.floor(minutes / 60), minutes % 60, 0, 0);

      return moved;
    };

    const startMinutes = Math.min(Math.max(start.getHours() * 60 + start.getMinutes(), openAt), closeAt);
    const endMinutes = Math.min(Math.max(end.getHours() * 60 + end.getMinutes(), openAt), closeAt);

    // Two shapes mean the same thing. A day-resolution column is one calendar day wide, so clicking it hands back
    // the whole drawn span — 06:00 to 20:00 — which is not a fourteen-hour entry anybody meant. A gesture that
    // fell entirely outside the window collapses to a point for a different reason but wants the same answer.
    // Neither carries a swept duration worth preserving, so both fall back to the morning and let the form's
    // presets say the rest.
    const coversWholeDay = endMinutes - startMinutes >= closeAt - openAt;

    return coversWholeDay
      ? {
          start: clamp(start, minutesOf(day.morningStart)),
          end: clamp(start, minutesOf(day.morningEnd)),
        }
      : { start: clamp(start, startMinutes), end: clamp(end, endMinutes) };
  }

  /**
   * The activity type a row stands for, where it stands for one.
   *
   * Only the non-project buckets carry a type in their row id; a project row's second half is a project id, and
   * reading it as a type code would seed the form with a GUID.
   */
  private typeCodeOf(row: BoardResource | undefined): string | null {
    if (!row || row.kind === 'project-line' || row.kind === 'category' || !row.parentId) {
      return null;
    }

    const code = row.id.split(':')[1] ?? null;

    return code === 'none' ? null : code;
  }

  /**
   * Drags the progress handle along the bar.
   *
   * Direct manipulation rather than a number field, because "about three-quarters" is how people actually think
   * about a task in flight, and the bar is already on screen showing them where it currently sits.
   */
  protected onProgressHandleDown(pointer: MouseEvent, event: MbscCalendarEvent): void {
    // Otherwise Mobiscroll reads the same press as the start of a move gesture and the block leaves its row.
    pointer.stopPropagation();
    pointer.preventDefault();

    const handle = pointer.target as HTMLElement;
    const bar = handle.closest('.progress__bar') as HTMLElement | null;
    const track = bar?.parentElement;

    if (!bar || !track) {
      return;
    }

    const width = track.offsetWidth;
    const originX = pointer.pageX;
    const origin = Number.parseInt(bar.style.width, 10) || 0;

    let latest = origin;

    const onMove = (move: MouseEvent) => {
      latest = Math.max(
        0,
        Math.min(100, Math.round(origin + ((move.pageX - originX) / width) * 100)),
      );

      // Written onto the event so the bar follows the pointer. The server has not agreed yet; this is the same
      // optimism a drag-to-move already runs on, and the reload after the call is what settles it.
      event['progress'] = latest;
      this.changes.markForCheck();
    };

    const onUp = () => {
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);

      // A click that never moved is not a progress change; emitting one would rewrite the number every time
      // somebody put a finger on the handle.
      if (latest !== origin && event.id) {
        this.progressed.emit({ eventId: String(event.id), percentComplete: latest });
      }
    };

    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  }

  private toResource(row: BoardResource): MbscResource {
    return {
      id: row.id,
      name: this.rowName(row),
      color: row.color ?? undefined,
      cssClass: `row row--${row.kind}`,
      // Only rows that resolve to one person accept a click-to-create. A lane on the personal board is one of
      // those — it is the caller's own week, sorted by bucket — but a unit or department header spans the people
      // beneath it, and a task planned "onto the header" would belong to nobody.
      // Categories included: clicking a header is the fastest way to say "something in this bucket", and the
      // form it opens is where the specific answer gets made. What a category may not do is *hold* an event —
      // the server always resolves an entry to a child row.
      eventCreation: ['person', 'project-line', 'lane', 'category'].includes(row.kind),
    };
  }

  /**
   * What a row is called, in the reader's language.
   *
   * A lane's `name` is its stable code — `project-build` — and the translation key for it arrives separately in
   * `subtitleKey`, because the department may relabel a bucket without renaming the code every report keys off.
   * Translating `name` therefore always missed and fell through to the code itself, which is how the personal
   * board came to list four lanes in machine-speak on a French-by-default product.
   *
   * A person's name is a person's name and is never translated.
   */
  private rowName(row: BoardResource): string {
    if (row.kind !== 'lane' && row.kind !== 'department' && row.kind !== 'category') {
      return row.name;
    }

    return this.label(row.subtitleKey ?? row.name);
  }

  /**
   * Translates a key, falling back to the key itself so an unmapped bucket still reads as something.
   *
   * Reads the language signal it does not otherwise need, and that read is the point. `translate()` is a one-shot
   * lookup with no reactive dependency, so the computeds that build rows and events had nothing to invalidate
   * them when somebody switched language — Mobiscroll's own month and day names changed while the row labels
   * beside them stayed in the previous language. Touching the signal here puts every caller on the right side of
   * that, in one place rather than in each computed that happens to render a label.
   */
  private label(key: string): string {
    this.language.language();

    const translated = this.transloco.translate(key);

    return translated === key ? key : translated;
  }
}
