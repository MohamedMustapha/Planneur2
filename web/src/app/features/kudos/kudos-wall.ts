import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  AnnualKudosView,
  KudoDirection,
  KudoPeriod,
  KudoScope,
  KudosStore,
  LeaderboardView,
  initialsOf,
} from '../../core/kudos/kudos.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { GiveKudo } from './give-kudo';

type KudosTab = 'wall' | 'leaderboard' | 'annual';

/**
 * The kudos screen.
 *
 * Three tabs over one store, and which of them exist depends on the department rather than on the viewer. A
 * department that counts has a wall and a review claim; one that scores also has a leaderboard. The tab is absent
 * rather than disabled where the mode does not enable it — a greyed-out leaderboard would advertise a ranking to
 * a department that decided against having one.
 */
@Component({
  selector: 'app-kudos-wall',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, DatePipe, PageHeader, GiveKudo],
  templateUrl: './kudos-wall.html',
  styleUrl: './kudos-wall.scss',
})
export class KudosWall {
  protected readonly kudos = inject(KudosStore);

  protected readonly tab = signal<KudosTab>('wall');
  protected readonly giving = signal(false);

  protected readonly leaderboard = signal<LeaderboardView | null>(null);
  protected readonly annual = signal<AnnualKudosView | null>(null);

  protected readonly year = signal(new Date().getFullYear());

  protected readonly scopes: readonly KudoScope[] = ['me', 'unit', 'department'];
  protected readonly directions: readonly KudoDirection[] = ['received', 'given'];
  protected readonly periods: readonly KudoPeriod[] = ['month', 'year'];

  protected readonly tabs = computed<readonly KudosTab[]>(() =>
    this.kudos.showsLeaderboard() ? ['wall', 'leaderboard', 'annual'] : ['wall', 'annual'],
  );

  protected readonly message = computed(() => {
    const failure = this.kudos.error() as { error?: { detail?: string; title?: string } } | undefined;

    return failure ? (failure.error?.detail ?? failure.error?.title ?? 'kudos.genericError') : null;
  });

  /** The per-person counter, which every mode has. Ordered by name on the server, never by score. */
  protected readonly totals = computed(() => this.kudos.summary()?.perPerson ?? []);

  protected async select(tab: KudosTab): Promise<void> {
    this.tab.set(tab);

    if (tab === 'leaderboard') {
      this.leaderboard.set(await this.kudos.leaderboard());
    }

    if (tab === 'annual') {
      this.annual.set(await this.kudos.annual(this.year()));
    }
  }

  protected async stepYear(by: number): Promise<void> {
    this.year.update((year) => year + by);
    this.annual.set(await this.kudos.annual(this.year()));
  }

  protected setScope(scope: KudoScope): void {
    this.kudos.setScope(scope);
  }

  protected setDirection(direction: KudoDirection): void {
    this.kudos.setDirection(direction);
  }

  protected async setPeriod(period: KudoPeriod): Promise<void> {
    this.kudos.setPeriod(period);

    if (this.tab() === 'leaderboard') {
      this.leaderboard.set(await this.kudos.leaderboard());
    }
  }

  protected async refresh(): Promise<void> {
    this.kudos.reload();

    if (this.tab() === 'leaderboard') {
      this.leaderboard.set(await this.kudos.leaderboard());
    }

    if (this.tab() === 'annual') {
      this.annual.set(await this.kudos.annual(this.year()));
    }
  }

  /** Initials, for a wall of cards that would otherwise be a wall of text. */
  protected initials(name: string | null): string {
    return initialsOf(name);
  }
}
