import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Mirrors `Cracra.Modules.Finance.Services` (v2 §04). */

export type CostMode = 'capex' | 'opex' | 'both';

export interface CostSplit {
  readonly capex: number;
  readonly opex: number;
  readonly excluded: number;
  readonly total: number;
}

export interface ConsolidatedNode {
  readonly nodeId: string;
  readonly parentId: string | null;
  readonly levelNo: number;
  readonly code: string;
  readonly name: string;
  readonly direct: CostSplit;
  readonly items: CostSplit;
  readonly own: CostSplit;
  readonly subtree: CostSplit;
  /** Null where nobody set an envelope — which is not the same as an envelope of zero. */
  readonly plannedAmount: number | null;
  readonly variance: number | null;
  readonly children: readonly ConsolidatedNode[];
}

export interface ConsolidatedView {
  readonly fiscalYear: number;
  readonly mode: CostMode;
  readonly node: ConsolidatedNode;
  /** True when the server picked the node, which is the "never starts empty" property. */
  readonly landed: boolean;
}

export interface LicenseView {
  readonly id: string;
  readonly nodeId: string;
  readonly itemId: string | null;
  readonly productName: string;
  readonly vendor: string | null;
  readonly seats: number;
  readonly unitCost: number;
  readonly annualCost: number;
  readonly currency: string;
  readonly billingCycle: string;
  readonly renewalDate: string | null;
  readonly active: boolean;
}

export interface ExternalWorkerView {
  readonly id: string;
  readonly nodeId: string;
  readonly itemId: string | null;
  readonly displayName: string;
  readonly vendor: string | null;
  readonly role: string | null;
  readonly rate: number;
  readonly rateUnit: string;
  readonly currency: string;
  readonly contractStart: string;
  readonly contractEnd: string | null;
  readonly active: boolean;
}

export interface CostComponentView {
  readonly id: string;
  readonly itemId: string | null;
  readonly nodeId: string | null;
  readonly ownerNodeId: string;
  readonly kind: CostKind;
  readonly label: string;
  readonly treatment: string;
  readonly treatmentOverridden: boolean;
  readonly amount: number;
  readonly currency: string;
  readonly periodStart: string;
  readonly periodEnd: string;
}

export type CostKind =
  | 'internal-effort'
  | 'license'
  | 'external-worker'
  | 'cloud'
  | 'hardware'
  | 'service-fee'
  | 'other';

export const COST_KINDS: readonly CostKind[] = [
  'internal-effort',
  'license',
  'external-worker',
  'cloud',
  'hardware',
  'service-fee',
  'other',
];

export const BILLING_CYCLES = ['monthly', 'quarterly', 'yearly', 'one-off'] as const;

export const RATE_UNITS = ['day', 'hour'] as const;

/** node or item — v2 §04.1. An item envelope is what a project's own budget is. */
export const BUDGET_SCOPES = ['node', 'item'] as const;

/** One row of the drill-down table, flattened with its depth so the template does not recurse. */
export interface ConsolidatedRow {
  readonly node: ConsolidatedNode;
  readonly depth: number;
  readonly hasChildren: boolean;
}

/**
 * The consolidated budget, as signals.
 *
 * The node is optional on purpose: omitted, the server lands on the highest node the caller heads, which is the
 * whole answer to v1's empty finance page. Drilling sets it, and the breadcrumb walks back up.
 */
@Injectable({ providedIn: 'root' })
export class ConsolidatedStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  readonly nodeId = signal<string | null>(null);
  readonly fiscalYear = signal<number>(new Date().getFullYear());
  readonly mode = signal<CostMode>('both');

  /** Node ids the reader has collapsed. Everything is open by default: the point is to see the shape. */
  readonly collapsed = signal<ReadonlySet<string>>(new Set());

  private readonly viewResource = httpResource<ConsolidatedView>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const params = new URLSearchParams();
    const nodeId = this.nodeId();

    if (nodeId) {
      params.set('nodeId', nodeId);
    }

    params.set('fy', String(this.fiscalYear()));
    params.set('mode', this.mode());

    return `/api/finance/consolidated?${params.toString()}`;
  });

  private readonly licensesResource = httpResource<readonly LicenseView[]>(() =>
    this.session.isAuthenticated() ? '/api/finance/licenses?within=90' : undefined,
  );

  private readonly externalsResource = httpResource<readonly ExternalWorkerView[]>(() =>
    this.session.isAuthenticated() ? '/api/finance/external-workers?within=90' : undefined,
  );

  readonly view = computed<ConsolidatedView | undefined>(() => this.viewResource.value());
  readonly isLoading = this.viewResource.isLoading;
  readonly hasError = computed(() => this.viewResource.error() !== undefined);

  readonly renewing = computed<readonly LicenseView[]>(() => this.licensesResource.value() ?? []);
  readonly expiring = computed<readonly ExternalWorkerView[]>(() => this.externalsResource.value() ?? []);

  /**
   * The table's rows, depth-first.
   *
   * Flattened here rather than recursed in the template because the number of levels is the deployment's, not the
   * component's — a template with three nested loops is exactly the fixed ladder §04.2 says not to build.
   */
  readonly rows = computed<readonly ConsolidatedRow[]>(() => {
    const root = this.view()?.node;

    return root ? this.flatten(root, 0) : [];
  });

  toggle(nodeId: string): void {
    this.collapsed.update((set) => {
      const next = new Set(set);

      if (!next.delete(nodeId)) {
        next.add(nodeId);
      }

      return next;
    });
  }

  drillTo(nodeId: string | null): void {
    this.nodeId.set(nodeId);
  }

  /**
   * Exports what is on screen and hands back where the file is.
   *
   * The server answers with a presigned link rather than bytes, so the download inherits an expiry — a URL
   * somebody forwards next month cannot outlive the entitlement that produced it.
   */
  reload(): void {
    this.viewResource.reload();
    this.licensesResource.reload();
    this.externalsResource.reload();
  }

  /** The cost lines behind one node's or one item's total. Asked for, not watched — it is a drawer's content. */
  async components(scope: { nodeId?: string; itemId?: string }): Promise<readonly CostComponentView[]> {
    const params = new URLSearchParams();

    if (scope.nodeId) {
      params.set('nodeId', scope.nodeId);
    }

    if (scope.itemId) {
      params.set('itemId', scope.itemId);
    }

    return firstValueFrom(
      this.http.get<readonly CostComponentView[]>(`/api/finance/components?${params.toString()}`),
    );
  }

  async saveBudget(payload: {
    scopeType: string;
    scopeId: string;
    fiscalYear: number;
    plannedAmount: number;
    currency: string | null;
    notes: string | null;
  }): Promise<void> {
    await firstValueFrom(this.http.post('/api/finance/budgets', payload));

    this.reload();
  }

  async addComponent(payload: {
    nodeId: string | null;
    itemId: string | null;
    kind: CostKind;
    label: string;
    amount: number;
    currency: string | null;
    periodStart: string;
    periodEnd: string;
    notes: string | null;
  }): Promise<void> {
    await firstValueFrom(this.http.post('/api/finance/components', payload));

    this.reload();
  }

  async addLicense(payload: {
    nodeId: string;
    itemId: string | null;
    productName: string;
    vendor: string | null;
    seats: number;
    unitCost: number;
    currency: string | null;
    billingCycle: string;
    renewalDate: string | null;
  }): Promise<void> {
    // A licence lays down its own cost component server-side, so nothing here has to remember to do it twice.
    await firstValueFrom(this.http.post('/api/finance/licenses', payload));

    this.reload();
  }

  async addExternalWorker(payload: {
    nodeId: string;
    itemId: string | null;
    displayName: string;
    vendor: string | null;
    role: string | null;
    rate: number;
    rateUnit: string;
    currency: string | null;
    contractStart: string;
    contractEnd: string | null;
  }): Promise<void> {
    await firstValueFrom(this.http.post('/api/finance/external-workers', payload));

    this.reload();
  }

  async export(): Promise<string> {
    const params = new URLSearchParams();
    const nodeId = this.nodeId();

    if (nodeId) {
      params.set('nodeId', nodeId);
    }

    params.set('fy', String(this.fiscalYear()));
    params.set('mode', this.mode());

    const exported = await firstValueFrom(
      this.http.get<{ url: string }>(`/api/finance/consolidated/export?${params.toString()}`),
    );

    return exported.url;
  }

  private flatten(node: ConsolidatedNode, depth: number): ConsolidatedRow[] {
    const rows: ConsolidatedRow[] = [
      { node, depth, hasChildren: node.children.length > 0 },
    ];

    if (this.collapsed().has(node.nodeId)) {
      return rows;
    }

    for (const child of node.children) {
      rows.push(...this.flatten(child, depth + 1));
    }

    return rows;
  }
}
