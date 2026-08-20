import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { KudosSummary } from '../../core/kudos/kudos.store';
import { SessionStore } from '../../core/session/session.store';

/**
 * Kudos this month, for the team board's summary strip.
 *
 * Its own resource rather than the shared store's, because this widget lives next to a board whose scope moves
 * independently of the kudos screen's — a lead stepping from their unit to the department must not leave the
 * kudos page pointing somewhere they did not put it.
 *
 * Counts, in the order the server sent them, which is by name. This is the one kudos surface people meet without
 * going looking for it, and sorting it by who has the most would put a leaderboard on the board of every
 * department that declined one.
 */
@Component({
  selector: 'app-kudos-monthly',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, RouterLink],
  templateUrl: './kudos-monthly.html',
  styleUrl: './kudos-monthly.scss',
})
export class KudosMonthly {
  private readonly session = inject(SessionStore);

  /** unit | department. The board's own scope, passed down rather than resolved again here. */
  readonly scope = input<'unit' | 'department'>('unit');
  readonly scopeId = input<string | null>(null);

  /** How many people the strip lists before it stops. The rest are a count, and the wall has all of them. */
  private static readonly Shown = 6;

  private readonly summaryResource = httpResource<KudosSummary>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const id = this.scopeId();

    return id
      ? `/api/kudos/summary?scope=${this.scope()}&scopeId=${id}&period=month`
      : `/api/kudos/summary?scope=${this.scope()}&period=month`;
  });

  protected readonly summary = computed(() => this.summaryResource.value());
  protected readonly total = computed(() => this.summary()?.total ?? 0);
  protected readonly showsPoints = computed(() => this.summary()?.showsPoints ?? false);

  protected readonly people = computed(() =>
    (this.summary()?.perPerson ?? []).slice(0, KudosMonthly.Shown),
  );

  protected readonly hidden = computed(() =>
    Math.max(0, (this.summary()?.perPerson.length ?? 0) - KudosMonthly.Shown),
  );
}
