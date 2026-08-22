import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { toWallClock } from '../../core/time/zoned';
import { MeetingsStore, UpcomingEntry } from '../../core/meetings/meetings.store';
import { CONTEXTUAL_ROLES } from '../../core/navigation/navigation';
import { SessionStore } from '../../core/session/session.store';

/**
 * "Coming up" — the next meetings and special days, on the dashboard rail.
 *
 * S0 put the heading and an empty line here, because a strip of invented meetings in a tool people use to plan
 * their week is worse than an honest blank. This fills it.
 *
 * One list, already merged and ordered by the server. Two calls and a client-side merge is exactly the assembly
 * that goes subtly wrong, and this sits on the dashboard where a second round trip is felt.
 */
@Component({
  selector: 'app-upcoming-strip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, RouterLink],
  templateUrl: './upcoming-strip.html',
  styleUrl: './upcoming-strip.scss',
})
export class UpcomingStrip {
  private readonly transloco = inject(TranslocoService);
  private readonly preferences = inject(PreferencesStore);

  private readonly session = inject(SessionStore);

  protected readonly meetings = inject(MeetingsStore);

  /**
   * Whether to offer the manager link.
   *
   * Rendering only, like every other role check on the client: what actually protects the manager is the endpoint
   * policy and the write predicate. Offering a link to somebody whose every action there would be refused is a
   * worse way to say no than not offering it.
   */
  protected readonly canManage = computed(() =>
    this.session.hasAny(
      CONTEXTUAL_ROLES.unitHead,
      CONTEXTUAL_ROLES.departmentHead,
      CONTEXTUAL_ROLES.projectLead,
      CONTEXTUAL_ROLES.productOwner,
      CONTEXTUAL_ROLES.pmo,
    ),
  );

  /** Short enough for a rail. What does not fit is on the boards, which is where planning actually happens. */
  private static readonly Shown = 6;

  protected readonly entries = computed(() =>
    this.meetings.upcoming().slice(0, UpcomingStrip.Shown),
  );

  protected readonly overflow = computed(() =>
    Math.max(0, this.meetings.upcoming().length - UpcomingStrip.Shown),
  );

  protected readonly failed = computed(() => this.meetings.upcomingError() !== undefined);

  /** Names arrive as keys or as free text; render whichever the dictionary can resolve. */
  protected label(entry: UpcomingEntry): string {
    const translated = this.transloco.translate(entry.nameKey);

    return translated === entry.nameKey ? entry.nameKey : translated;
  }

  /**
   * The date, and the time only where there is one.
   *
   * A special day is a date, not an instant — rendering "00:00" beside a patch party would be both meaningless
   * and, in a browser west of UTC, the wrong day.
   */
  protected when(entry: UpcomingEntry): string {
    // Read in the person's own zone: a 09:00 stand-up is 09:00 for whoever is attending it, and a colleague in
    // another country opening the same strip should see the hour they are expected to join at.
    const at = toWallClock(entry.at, this.preferences.timeZone());
    const day = at.toLocaleDateString(this.transloco.getActiveLang(), {
      day: '2-digit',
      month: 'short',
    });

    return entry.allDay
      ? day
      : `${day} · ${at.toLocaleTimeString(this.transloco.getActiveLang(), { hour: '2-digit', minute: '2-digit' })}`;
  }

  protected severityClass(entry: UpcomingEntry): string {
    return `upcoming__dot--${entry.severity ?? 'meeting'}`;
  }
}
