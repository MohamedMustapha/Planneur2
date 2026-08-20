import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ActivityEntryView } from '../../core/activities/activities.store';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { SessionStore } from '../../core/session/session.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ScopeSelector } from '../../shared/ui/scope-selector/scope-selector';
import { SmokeTimeline } from '../../shared/timeline/smoke-timeline/smoke-timeline';
import { ActivitiesStore } from '../../core/activities/activities.store';
import { QuickAdd } from './quick-add';
import { UpcomingStrip } from '../meetings/upcoming-strip';

type BoardTab = 'week' | 'month' | 'list';

/**
 * "Mon tableau" — the daily activity-logging screen, and the shell's reference page.
 *
 * S0 built the frame and left every number reading zero, because showing invented figures in a tool whose whole
 * purpose is accurate time reporting would have been exactly the wrong kind of placeholder. S5 fills it in: the
 * meter, the tiles and the day list are now this person's real week, and quick add writes to it.
 *
 * S6 still owns the drag-to-log timeline; until then the entries are listed by day, which is the same information
 * without the geometry.
 */
@Component({
  selector: 'app-board',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader, ScopeSelector, SmokeTimeline, QuickAdd, UpcomingStrip],
  templateUrl: './board.html',
  styleUrl: './board.scss',
})
export class Board {
  protected readonly session = inject(SessionStore);
  protected readonly departments = inject(DepartmentScopeStore);
  protected readonly directory = inject(DirectoryStore);

  protected readonly activities = inject(ActivitiesStore);

  protected readonly activeTab = signal<BoardTab>('week');

  protected readonly quickAddOpen = signal(false);

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

  /** The week's entries grouped by day, in the order the days fall. */
  protected readonly days = computed(() => {
    const groups = new Map<string, { day: string; entries: ActivityEntryView[] }>();

    for (const entry of this.activities.entries()) {
      const day = entry.slotStart.slice(0, 10);

      (groups.get(day) ?? groups.set(day, { day, entries: [] }).get(day)!).entries.push(entry);
    }

    return [...groups.values()].sort((left, right) => left.day.localeCompare(right.day));
  });

  protected selectTab(tab: BoardTab): void {
    this.activeTab.set(tab);
  }

  protected openQuickAdd(): void {
    this.quickAddOpen.set(true);
  }

  protected closeQuickAdd(): void {
    this.quickAddOpen.set(false);
  }

  protected async remove(entryId: string): Promise<void> {
    await this.activities.remove(entryId);
  }
}
