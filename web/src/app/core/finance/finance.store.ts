import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Mirrors `Cracra.Modules.Finance.Services`. */

export type FinanceScope = 'department' | 'project' | 'portfolio';

export type FinancePeriodKind = 'month' | 'quarter' | 'year' | 'custom';

export type Treatment = 'capex' | 'opex' | 'excluded';

export const TREATMENTS: readonly Treatment[] = ['capex', 'opex', 'excluded'];

export const FINANCE_PERIODS: readonly FinancePeriodKind[] = ['month', 'quarter', 'year'];

export interface BucketTotal {
  readonly bucket: string;
  readonly treatment: Treatment;
  readonly hours: number;
  readonly effortCost: number;
  /** Hours no rate card covered. Non-zero means the bucket's money understates it. */
  readonly unvaluedHours: number;
}

export interface SplitTotals {
  readonly capexAmount: number;
  readonly opexAmount: number;
  readonly excludedAmount: number;
  /** Money the rules left in neither column: excluded buckets, and mixed projects with no effort. */
  readonly unallocatedAmount: number;
  readonly manualCapex: number;
  readonly manualOpex: number;
  readonly effortCapex: number;
  readonly effortOpex: number;
  readonly capexHours: number;
  readonly opexHours: number;
  readonly excludedHours: number;
  readonly unclassifiedHours: number;
  /** False where no rate card priced anything — the money columns are then empty rather than zero. */
  readonly effortValued: boolean;
  readonly byBucket: readonly BucketTotal[];
  readonly totalHours: number;
  readonly capexRatio: number | null;
}

export interface ProjectLine {
  readonly projectId: string;
  readonly code: string;
  readonly name: string;
  readonly classification: string;
  readonly manualCost: number;
  readonly costCurrency: string;
  /** False where the cost is in another currency than the view's, and so is not in its totals. */
  readonly costCounted: boolean;
  readonly buildHours: number;
  readonly runHours: number;
  readonly effortCost: number;
  readonly capexAmount: number;
  readonly opexAmount: number;
}

export interface PeriodLine {
  readonly month: string;
  readonly capexHours: number;
  readonly opexHours: number;
  readonly excludedHours: number;
  readonly capexCost: number;
  readonly opexCost: number;
}

export interface CapexOpexRule {
  readonly departmentId: string;
  readonly buildTreatment: Treatment;
  readonly runTreatment: Treatment;
  readonly qolTreatment: Treatment;
  readonly adminTreatment: Treatment;
  /** False where these are the platform's defaults rather than something somebody chose. */
  readonly configured: boolean;
}

export interface CapexOpexView {
  readonly scope: FinanceScope;
  readonly scopeId: string | null;
  readonly scopeLabel: string;
  readonly periodKind: FinancePeriodKind;
  readonly from: string;
  readonly to: string;
  readonly currency: string;
  readonly otherCurrencyProjects: number;
  readonly totals: SplitTotals;
  readonly projects: readonly ProjectLine[];
  readonly periods: readonly PeriodLine[];
  readonly rule: CapexOpexRule;
}

export interface RateCard {
  readonly id: string;
  readonly departmentId: string;
  readonly functionalRoleId: string;
  readonly functionalRoleCode: string | null;
  readonly hourlyRate: number;
  readonly currency: string;
  readonly effectiveFrom: string;
  /** Exclusive, and null for "still current". */
  readonly effectiveTo: string | null;
}

export interface RateCardPayload {
  readonly id?: string;
  readonly departmentId: string;
  readonly functionalRoleId: string;
  readonly hourlyRate: number;
  readonly currency: string;
  readonly effectiveFrom: string;
  readonly effectiveTo: string | null;
}

export interface CapexOpexExport {
  readonly format: string;
  readonly contentType: string;
  readonly key: string;
  readonly url: string;
  readonly expiresAt: string;
}

/**
 * The capitalization view, and the two knobs behind it.
 *
 * Everything here is head-only at the API, so a member who reaches this store — by typing the URL, since the rail
 * hides the entry — gets a 403 rather than an empty view. That is the server's decision showing through, and the
 * screen renders it as a refusal rather than pretending the department had a quiet month.
 */
@Injectable({ providedIn: 'root' })
export class FinanceStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  readonly scope = signal<FinanceScope>('department');
  readonly scopeId = signal<string | null>(null);
  readonly period = signal<FinancePeriodKind>('month');

  /** The day inside the period being looked at. The server derives the month, quarter or year around it. */
  readonly anchor = signal(new Date().toISOString().slice(0, 10));

  private readonly viewResource = httpResource<CapexOpexView>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const scopeId = this.scopeId();

    // A project scope with nothing selected would ask the server for "some project", so it simply does not ask.
    if (this.scope() === 'project' && !scopeId) {
      return undefined;
    }

    return (
      `/api/finance/capex-opex?scope=${this.scope()}&period=${this.period()}&from=${this.anchor()}` +
      (scopeId ? `&scopeId=${scopeId}` : '')
    );
  });

  private readonly rateCardsResource = httpResource<readonly RateCard[]>(() =>
    this.session.isAuthenticated() ? '/api/finance/rate-cards' : undefined,
  );

  readonly view = computed(() => this.viewResource.value());
  readonly rateCards = computed<readonly RateCard[]>(() => this.rateCardsResource.value() ?? []);

  readonly isLoading = computed(() => this.viewResource.isLoading());
  readonly error = computed(() => this.viewResource.error());

  /** The rule the current view was computed with, so the editor and the figures cannot disagree. */
  readonly rule = computed(() => this.view()?.rule);

  async saveRule(rule: Omit<CapexOpexRule, 'configured'>): Promise<void> {
    await firstValueFrom(this.http.put('/api/finance/rules', rule));
    this.reload();
  }

  async saveRateCard(payload: RateCardPayload): Promise<void> {
    await firstValueFrom(this.http.put('/api/finance/rate-cards', payload));
    this.reloadRateCards();
    this.reload();
  }

  async deleteRateCard(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/finance/rate-cards/${id}`));
    this.reloadRateCards();
    this.reload();
  }

  /**
   * Exports the current view and returns the link.
   *
   * The link is short-lived and is shown rather than followed silently: a download that starts on its own is
   * indistinguishable from one that failed, and this one is worth being sure about.
   */
  async export(): Promise<CapexOpexExport> {
    const scopeId = this.scopeId();

    return firstValueFrom(
      this.http.get<CapexOpexExport>(
        `/api/finance/capex-opex/export?format=xlsx&scope=${this.scope()}` +
          `&period=${this.period()}&from=${this.anchor()}` +
          (scopeId ? `&scopeId=${scopeId}` : ''),
      ),
    );
  }

  reload(): void {
    this.viewResource.reload();
  }

  reloadRateCards(): void {
    this.rateCardsResource.reload();
  }
}
