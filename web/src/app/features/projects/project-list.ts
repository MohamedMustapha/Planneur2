import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LocalizedNumber } from '../../core/i18n/localized-number.pipe';
import { RouterLink } from '@angular/router';
import { DecimalPipe } from '@angular/common';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProjectsStore, ProjectSummary } from '../../core/projects/projects.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The project list, scoped to the department in the top bar.
 *
 * Classification drives the accent colour straight from the design tokens, so a BUILD project reads the same
 * indigo here as its blocks do on every timeline.
 */
@Component({
  selector: 'app-project-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LocalizedNumber, TranslocoDirective, RouterLink, DecimalPipe, PageHeader],
  templateUrl: './project-list.html',
  styleUrl: './project-list.scss',
})
export class ProjectList {
  protected readonly projects = inject(ProjectsStore);

  protected readonly filter = signal<string>('all');

  protected readonly filters = ['all', 'build', 'run', 'mixed'] as const;

  protected readonly visible = computed<readonly ProjectSummary[]>(() => {
    const classification = this.filter();

    return classification === 'all'
      ? this.projects.projects()
      : this.projects.projects().filter((project) => project.classification === classification);
  });

  protected readonly totalCost = computed(() =>
    this.visible().reduce((sum, project) => sum + project.costAmount, 0),
  );

  protected select(classification: string): void {
    this.filter.set(classification);
  }
}
