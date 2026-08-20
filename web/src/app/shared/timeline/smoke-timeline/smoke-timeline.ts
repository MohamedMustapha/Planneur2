import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MbscEventcalendarOptions, MbscLocale, MbscModule } from '@mobiscroll/angular';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageStore } from '../../../core/i18n/language.store';
import { LayoutStore } from '../../../core/layout/layout.store';
import { workWeek } from '../../../core/time/week';

/**
 * The S0 Mobiscroll proof: a licensed timeline, rendering an empty working week, wired to the shell's week pager
 * and language.
 *
 * It carries no events on purpose. S6 owns the three real timeline archetypes (RUN work orders, RUN shifts, BUILD
 * task progress); what this component has to establish now is only that the licensed package builds, themes with
 * our tokens, and localises — the three things that are expensive to discover late.
 */
@Component({
  selector: 'app-smoke-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MbscModule, TranslocoDirective],
  templateUrl: './smoke-timeline.html',
  styleUrl: './smoke-timeline.scss',
})
export class SmokeTimeline {
  private readonly layout = inject(LayoutStore);
  private readonly language = inject(LanguageStore);

  protected readonly selectedDate = computed(() => workWeek(this.layout.weekOffset()).monday);

  protected readonly options = computed<MbscEventcalendarOptions>(() => ({
    locale: this.mobiscrollLocale(),
    theme: 'material',
    themeVariant: 'auto',
    view: {
      timeline: {
        type: 'week',
        // 07:00 → 19:00 in one-hour steps, matching the grid in the design.
        startDay: 1,
        endDay: 5,
        startTime: '07:00',
        endTime: '19:00',
        timeCellStep: 60,
        timeLabelStep: 120,
      },
    },
    data: [],
    resources: [],
  }));

  private mobiscrollLocale(): MbscLocale | undefined {
    // Mobiscroll ships its locales on the global namespace of the bundle; resolving by code keeps this to one
    // lookup rather than three static imports of dictionaries we may not use.
    const locales = (globalThis as Record<string, unknown>)['mobiscroll'] as Record<string, unknown> | undefined;

    return locales?.[`locale${this.language.language().toUpperCase()}`] as MbscLocale | undefined;
  }
}
