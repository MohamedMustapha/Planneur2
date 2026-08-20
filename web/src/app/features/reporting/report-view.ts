import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import {
  ReportMetric,
  ReportPeriodKind,
  ReportScope,
  ReportSection,
  ReportingStore,
} from '../../core/reporting/reporting.store';
import { ProjectsStore } from '../../core/projects/projects.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The contextual status report.
 *
 * Two halves that behave differently on purpose. The figures and tables are computed server-side in code and
 * render the moment they arrive; the narrative is written by a local model, arrives token by token, and is
 * labelled as machine-written wherever it appears. Somebody skimming this screen should never be in doubt about
 * which half they are reading.
 */
@Component({
  selector: 'app-report-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, PageHeader],
  templateUrl: './report-view.html',
  styleUrl: './report-view.scss',
})
export class ReportView {
  private readonly transloco = inject(TranslocoService);

  protected readonly reporting = inject(ReportingStore);
  protected readonly projects = inject(ProjectsStore);

  protected readonly periods: readonly ReportPeriodKind[] = ['week', 'month'];

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The link the last export produced. Short-lived, which is why it is shown rather than followed silently. */
  protected readonly exportUrl = signal<string | null>(null);

  protected readonly needsProject = computed(() => this.reporting.scope() === 'project');

  protected readonly awaitingProject = computed(
    () => this.needsProject() && !this.reporting.scopeId(),
  );

  /** Whatever went wrong: an action's refusal, or the report itself failing to load. */
  protected readonly message = computed(() => {
    const refused = this.error();

    if (refused) {
      return refused;
    }

    const failure = this.reporting.error() as
      { error?: { detail?: string; title?: string } } | undefined;

    return failure
      ? (failure.error?.detail ?? failure.error?.title ?? 'reports.genericError')
      : null;
  });

  protected readonly periodLabel = computed(() => {
    const period = this.reporting.report()?.period;

    if (!period) {
      return '';
    }

    return period.isoWeek
      ? this.transloco.translate(period.labelKey, { week: period.isoWeek, year: period.isoYear })
      : `${period.from} – ${period.to}`;
  });

  protected select(scope: ReportScope): void {
    this.error.set(null);
    this.exportUrl.set(null);
    this.reporting.show(scope);
  }

  protected selectProject(projectId: string): void {
    this.reporting.show('project', projectId || null);
  }

  protected setPeriod(period: ReportPeriodKind): void {
    this.exportUrl.set(null);
    this.reporting.setPeriod(period);
  }

  protected step(by: number): void {
    this.exportUrl.set(null);
    this.reporting.step(by);
  }

  /** Translates a key, falling back to the literal — labels are keys or data and the server does not say which. */
  protected label(value: string): string {
    const translated = this.transloco.translate(value);

    return translated === value ? value : translated;
  }

  /**
   * Formats a figure by its declared unit.
   *
   * The server sends a number and a unit rather than a formatted string, because how 35.5 hours reads is a
   * question about this browser's locale and the server has no idea what it is.
   */
  protected format(metric: ReportMetric): string {
    const value = metric.value.toLocaleString(this.transloco.getActiveLang(), {
      maximumFractionDigits: 2,
    });

    switch (metric.unit) {
      case 'hours':
        return `${value} h`;
      case 'percent':
        return `${value} %`;
      case 'currency':
        return `${value} €`;
      default:
        return value;
    }
  }

  /** True when a section has nothing to show — a quiet period rather than a failure to load. */
  protected isEmpty(section: ReportSection): boolean {
    return (
      section.metrics.length === 0 &&
      section.notes.length === 0 &&
      section.tables.every((table) => table.rows.length === 0)
    );
  }

  protected number(value: number): string {
    return value.toLocaleString(this.transloco.getActiveLang(), { maximumFractionDigits: 2 });
  }

  protected async generate(force = false): Promise<void> {
    await this.run(() => this.reporting.generate(force));
  }

  protected async export(): Promise<void> {
    await this.run(async () => {
      const result = await this.reporting.export('pdf');

      this.exportUrl.set(result.url);
    });
  }

  /**
   * Runs an action and surfaces the refusal.
   *
   * Every one of these can legitimately be refused — a scope this viewer does not hold, a model that answered
   * nothing — and the server's own sentence says which and what would fix it.
   */
  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (failure: unknown) {
      const problem = failure as { error?: { detail?: string; title?: string } };

      this.error.set(problem.error?.detail ?? problem.error?.title ?? 'reports.genericError');
    } finally {
      this.busy.set(false);
    }
  }
}
