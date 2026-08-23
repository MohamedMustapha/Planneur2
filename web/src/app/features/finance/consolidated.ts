import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { ConsolidatedStore, CostMode } from '../../core/finance/consolidated.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The consolidated budget (v2 §04.2).
 *
 * One table, four columns, and as many rows deep as the tree happens to be. The depth is the deployment's, so the
 * rows are flattened in the store and rendered in one loop — a template with a fixed number of nested levels is
 * the three-rung ladder the spec exists to replace.
 */
@Component({
  selector: 'app-consolidated',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, DecimalPipe, FormsModule, PageHeader],
  templateUrl: './consolidated.html',
  styleUrl: './consolidated.scss',
})
export class Consolidated {
  protected readonly finance = inject(ConsolidatedStore);

  protected readonly modes: readonly CostMode[] = ['both', 'capex', 'opex'];

  protected readonly years = computed(() => {
    const current = new Date().getFullYear();

    return [current + 1, current, current - 1, current - 2];
  });

  protected setMode(value: string): void {
    this.finance.mode.set(value as CostMode);
  }

  protected setYear(value: string): void {
    this.finance.fiscalYear.set(Number(value));
  }

  /** Indentation is the tree, so it is an inline style rather than a class per level nobody can enumerate. */
  protected indent(depth: number): string {
    return `${depth * 1.25}rem`;
  }

  protected isCollapsed(nodeId: string): boolean {
    return this.finance.collapsed().has(nodeId);
  }

  protected readonly exporting = signal(false);

  /**
   * Opens the exported workbook.
   *
   * A new tab rather than an anchor with a download attribute: the file lives in object storage behind a
   * presigned URL, so the browser is fetching from another origin and the attribute would be ignored anyway.
   */
  protected async export(): Promise<void> {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);

    try {
      const url = await this.finance.export();

      window.open(url, '_blank', 'noopener');
    } finally {
      this.exporting.set(false);
    }
  }
}
