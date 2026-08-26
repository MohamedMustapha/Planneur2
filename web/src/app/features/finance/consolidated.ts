import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LocalizedNumber } from '../../core/i18n/localized-number.pipe';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  BILLING_CYCLES,
  BUDGET_SCOPES,
  COST_KINDS,
  ConsolidatedNode,
  ConsolidatedStore,
  CostComponentView,
  CostKind,
  CostMode,
  RATE_UNITS,
} from '../../core/finance/consolidated.store';
import { CatalogStore } from '../../core/portfolio/catalog.store';
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
  imports: [LocalizedNumber, TranslocoDirective, DecimalPipe, FormsModule, PageHeader],
  templateUrl: './consolidated.html',
  styleUrl: './consolidated.scss',
})
export class Consolidated {
  protected readonly finance = inject(ConsolidatedStore);

  protected readonly catalog = inject(CatalogStore);

  protected readonly modes: readonly CostMode[] = ['both', 'capex', 'opex'];

  protected readonly kinds = COST_KINDS;
  protected readonly cycles = BILLING_CYCLES;
  protected readonly rateUnits = RATE_UNITS;
  protected readonly budgetScopes = BUDGET_SCOPES;

  // --- The editor -----------------------------------------------------------------------------------------------
  //
  // A drawer on the node the reader drilled to, not a separate screen. Everything a head records here — the
  // envelope, a cloud bill, a licence, a consultant — lands under that node, and reading the total they are about
  // to change while they change it is the whole reason it is not a settings page.

  protected readonly managing = signal<ConsolidatedNode | null>(null);
  protected readonly tab = signal<'envelope' | 'costs' | 'licenses' | 'externals'>('envelope');
  protected readonly busy = signal(false);
  protected readonly failure = signal(false);
  protected readonly lines = signal<readonly CostComponentView[]>([]);

  /** node or item: an item envelope is a project's own budget, which §04.1 has and no screen could set. */
  protected readonly budgetScope = signal<'node' | 'item'>('node');
  protected readonly budgetItemId = signal('');
  protected readonly budgetAmount = signal<number | null>(null);
  protected readonly budgetNotes = signal('');

  protected readonly costKind = signal<CostKind>('cloud');
  protected readonly costLabel = signal('');
  protected readonly costAmount = signal<number | null>(null);
  protected readonly costItemId = signal('');
  protected readonly costFrom = signal('');
  protected readonly costTo = signal('');

  protected readonly licenseName = signal('');
  protected readonly licenseVendor = signal('');
  protected readonly licenseSeats = signal<number | null>(null);
  protected readonly licenseUnitCost = signal<number | null>(null);
  protected readonly licenseCycle = signal<string>('yearly');
  protected readonly licenseRenewal = signal('');

  protected readonly externalName = signal('');
  protected readonly externalVendor = signal('');
  protected readonly externalRole = signal('');
  protected readonly externalRate = signal<number | null>(null);
  protected readonly externalRateUnit = signal<string>('day');
  protected readonly externalStart = signal('');
  protected readonly externalEnd = signal('');

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

  protected async manage(node: ConsolidatedNode): Promise<void> {
    this.managing.set(node);
    this.tab.set('envelope');
    this.failure.set(false);
    this.budgetScope.set('node');
    this.budgetItemId.set('');
    this.budgetAmount.set(node.plannedAmount);
    this.budgetNotes.set('');

    const year = this.finance.fiscalYear();

    this.costFrom.set(`${year}-01-01`);
    this.costTo.set(`${year}-12-31`);

    this.lines.set(await this.finance.components({ nodeId: node.nodeId }));
  }

  protected closeEditor(): void {
    this.managing.set(null);
  }

  protected setTab(value: 'envelope' | 'costs' | 'licenses' | 'externals'): void {
    this.tab.set(value);
  }

  protected setBudgetScope(value: string): void {
    this.budgetScope.set(value as 'node' | 'item');
  }

  protected setCostKind(value: string): void {
    this.costKind.set(value as CostKind);
  }

  protected async saveBudget(): Promise<void> {
    const node = this.managing();
    const amount = this.budgetAmount();

    if (!node || amount === null) {
      return;
    }

    const scopeId = this.budgetScope() === 'item' ? this.budgetItemId() : node.nodeId;

    if (!scopeId) {
      return;
    }

    await this.run(() =>
      this.finance.saveBudget({
        scopeType: this.budgetScope(),
        scopeId,
        fiscalYear: this.finance.fiscalYear(),
        plannedAmount: amount,
        currency: null,
        notes: blank(this.budgetNotes()),
      }),
    );
  }

  protected async addCost(): Promise<void> {
    const node = this.managing();
    const amount = this.costAmount();

    if (!node || amount === null || this.costLabel().trim().length === 0) {
      return;
    }

    const item = blank(this.costItemId());

    await this.run(async () => {
      // A cost line hangs off an item or a node, never both: the consolidation adds them once at the node the
      // item belongs to, and a line claimed twice would be counted twice.
      await this.finance.addComponent({
        nodeId: item ? null : node.nodeId,
        itemId: item,
        kind: this.costKind(),
        label: this.costLabel().trim(),
        amount,
        currency: null,
        periodStart: this.costFrom(),
        periodEnd: this.costTo(),
        notes: null,
      });

      this.costLabel.set('');
      this.costAmount.set(null);
      this.lines.set(await this.finance.components({ nodeId: node.nodeId }));
    });
  }

  protected async addLicense(): Promise<void> {
    const node = this.managing();
    const unitCost = this.licenseUnitCost();
    const seats = this.licenseSeats();

    if (!node || unitCost === null || seats === null || this.licenseName().trim().length === 0) {
      return;
    }

    await this.run(async () => {
      await this.finance.addLicense({
        nodeId: node.nodeId,
        itemId: null,
        productName: this.licenseName().trim(),
        vendor: blank(this.licenseVendor()),
        seats,
        unitCost,
        currency: null,
        billingCycle: this.licenseCycle(),
        renewalDate: blank(this.licenseRenewal()),
      });

      this.licenseName.set('');
      this.licenseSeats.set(null);
      this.licenseUnitCost.set(null);
      this.lines.set(await this.finance.components({ nodeId: node.nodeId }));
    });
  }

  protected async addExternal(): Promise<void> {
    const node = this.managing();
    const rate = this.externalRate();

    if (!node || rate === null || this.externalName().trim().length === 0 || !this.externalStart()) {
      return;
    }

    await this.run(async () => {
      await this.finance.addExternalWorker({
        nodeId: node.nodeId,
        itemId: null,
        displayName: this.externalName().trim(),
        vendor: blank(this.externalVendor()),
        role: blank(this.externalRole()),
        rate,
        rateUnit: this.externalRateUnit(),
        currency: null,
        contractStart: this.externalStart(),
        contractEnd: blank(this.externalEnd()),
      });

      this.externalName.set('');
      this.externalRate.set(null);
    });
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.failure.set(false);
    this.busy.set(true);

    try {
      await work();
    } catch {
      this.failure.set(true);
    } finally {
      this.busy.set(false);
    }
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

function blank(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}
