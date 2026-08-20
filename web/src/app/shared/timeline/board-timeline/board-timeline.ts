import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import {
  MbscCalendarEvent,
  MbscEventcalendarOptions,
  MbscEventCreateEvent,
  MbscEventUpdateEvent,
  MbscLocale,
  MbscModule,
  MbscResource,
} from '@mobiscroll/angular';
import { TranslocoService } from '@jsverse/transloco';
import {
  BoardArchetype,
  BoardEvent,
  BoardOverlay,
  BoardResource,
} from '../../../core/scheduling/scheduling.store';
import { LanguageStore } from '../../../core/i18n/language.store';

/** A block the user dragged, in the shape the caller needs to persist it. */
export interface TimelineMove {
  readonly eventId: string;
  readonly resourceId: string;
  readonly start: Date;
  readonly end: Date;
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
  private readonly language = inject(LanguageStore);
  private readonly transloco = inject(TranslocoService);

  readonly archetype = input.required<BoardArchetype>();
  readonly resources = input.required<readonly BoardResource[]>();
  readonly events = input.required<readonly BoardEvent[]>();
  readonly overlays = input<readonly BoardOverlay[]>([]);
  readonly from = input.required<string>();
  readonly to = input.required<string>();

  /** False puts the whole canvas in read-only mode, whatever individual events say. */
  readonly editable = input(false);

  readonly moved = output<TimelineMove>();

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
      start: new Date(event.start),
      end: new Date(event.end),
      color: event.color ?? undefined,
      cssClass: event.cssClass ?? undefined,
      // Two gates, and both must open: the board's own read-only flag and the event's. A department board is
      // read-only entirely; a personal board is editable but its actuals are not.
      editable: this.editable() && event.editable,
      dragBetweenResources: this.archetype() === 'work-orders' && this.editable() && event.editable,
      progress: event.progress ?? undefined,
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
      start: new Date(overlay.from),
      end: new Date(`${overlay.to}T23:59:59`),
      background: overlay.color ?? 'var(--surface-2)',
      title: overlay.title,
    })),
  );

  protected readonly options = computed<MbscEventcalendarOptions>(() => ({
    locale: this.mobiscrollLocale(),
    theme: 'material',
    themeVariant: 'auto',
    clickToCreate: false,
    dragToCreate: false,
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
        // The shift board needs half-hour resolution to show a 12:30 handover; the others read better at an hour.
        timeCellStep: this.archetype() === 'shifts' ? 30 : 60,
        timeLabelStep: this.archetype() === 'shifts' ? 60 : 120,
        startTime: '07:00',
        endTime: '20:00',
        // Progress bars need the room; the other archetypes stay compact so a unit of twelve fits on a screen.
        rowHeight: this.archetype() === 'task-progress' ? 'variable' : 'equal',
      },
    },
  }));

  protected readonly selectedDate = computed(() => new Date(this.from()));

  protected onEventUpdate(args: MbscEventUpdateEvent): boolean {
    const event = args.event;

    if (!event?.id || !event.start || !event.end) {
      return false;
    }

    this.moved.emit({
      eventId: String(event.id),
      resourceId: String(args.resource ?? event.resource ?? ''),
      start: new Date(event.start as string | Date),
      end: new Date(event.end as string | Date),
    });

    return true;
  }

  /**
   * Refuses in-place creation.
   *
   * Creating on the canvas would bypass the activity form, and with it the type picker, the project requirement
   * and the 35h guardrail. A block appears here because something was logged or assigned, never the other way
   * round.
   */
  protected onEventCreate(_: MbscEventCreateEvent): boolean {
    return false;
  }

  private toResource(row: BoardResource): MbscResource {
    return {
      id: row.id,
      name: row.kind === 'lane' || row.kind === 'department' ? this.label(row.name) : row.name,
      color: row.color ?? undefined,
      cssClass: `row row--${row.kind}`,
    };
  }

  /** Translates a key, falling back to the key itself so an unmapped bucket still reads as something. */
  private label(key: string): string {
    const translated = this.transloco.translate(key);

    return translated === key ? key : translated;
  }

  private mobiscrollLocale(): MbscLocale | undefined {
    const locales = (globalThis as Record<string, unknown>)['mobiscroll'] as Record<string, unknown> | undefined;

    return locales?.[`locale${this.language.language().toUpperCase()}`] as MbscLocale | undefined;
  }
}
