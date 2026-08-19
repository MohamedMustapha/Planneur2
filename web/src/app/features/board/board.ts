import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { SessionStore } from '../../core/session/session.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ScopeSelector } from '../../shared/ui/scope-selector/scope-selector';
import { SmokeTimeline } from '../../shared/timeline/smoke-timeline/smoke-timeline';

type BoardTab = 'week' | 'month' | 'list';

/**
 * "Mon tableau" — the daily activity-logging screen, and the shell's reference page.
 *
 * S0 builds the frame: header, the 35h meter, the tab strip, the timeline surface, the summary tiles and the unit
 * side rail. Every number below reads zero and every list is empty because there is no Activities module yet, and
 * showing invented figures in a tool whose whole purpose is accurate time reporting would be exactly the wrong
 * kind of placeholder. S5 supplies the data; S6 replaces the smoke timeline with the real drag-to-log surface.
 */
@Component({
  selector: 'app-board',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader, ScopeSelector, SmokeTimeline],
  templateUrl: './board.html',
  styleUrl: './board.scss',
})
export class Board {
  protected readonly session = inject(SessionStore);
  protected readonly departments = inject(DepartmentScopeStore);

  protected readonly activeTab = signal<BoardTab>('week');

  protected readonly tabs: readonly { id: BoardTab; labelKey: string }[] = [
    { id: 'week', labelKey: 'board.tabs.week' },
    { id: 'month', labelKey: 'board.tabs.month' },
    { id: 'list', labelKey: 'board.tabs.list' },
  ];

  /** The department-configurable weekly target. S5 reads it from department configuration; 35h is the default. */
  protected readonly weeklyTargetHours = 35;

  protected readonly loggedHours = signal(0);

  protected readonly meterPercent = computed(() =>
    Math.min(100, (this.loggedHours() / this.weeklyTargetHours) * 100),
  );

  /**
   * Blue while filling, green once the week is essentially complete, amber past the target. The amber state is a
   * soft warning by default; a department that enforces the cap turns it into a block in S5.
   */
  protected readonly meterColor = computed(() => {
    const logged = this.loggedHours();

    if (logged > this.weeklyTargetHours) {
      return 'var(--warning)';
    }

    return logged >= this.weeklyTargetHours - 2 ? 'var(--success)' : 'var(--primary)';
  });

  protected readonly subtitle = computed(() => {
    const parts = [this.session.displayName(), this.departments.selected()?.name].filter(Boolean);

    return parts.join(' · ');
  });

  protected readonly summaryTiles = computed(() => [
    { id: 'build', labelKey: 'board.summary.build', color: 'var(--activity-build)' },
    { id: 'run', labelKey: 'board.summary.run', color: 'var(--activity-run)' },
    { id: 'qol', labelKey: 'board.summary.qolTraining', color: 'var(--activity-qol)' },
    { id: 'variance', labelKey: 'board.summary.variance', color: 'var(--ink)' },
  ]);

  protected selectTab(tab: BoardTab): void {
    this.activeTab.set(tab);
  }
}
