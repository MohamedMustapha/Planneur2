import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { zonedDay } from '../../core/time/zoned';
import {
  FINANCE_PERIODS,
  FinancePeriodKind,
  FinanceScope,
  FinanceStore,
  RateCard,
  TREATMENTS,
  Treatment,
} from '../../core/finance/finance.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { ProjectsStore } from '../../core/projects/projects.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/** The four buckets, in the order the rule editor lists them. */
const BUCKETS = ['build', 'run', 'qol', 'admin'] as const;

type Bucket = (typeof BUCKETS)[number];

/**
 * The capitalization view: capex against opex, by project and by month, with the two knobs that decide it.
 *
 * Head-only, and the screen says so rather than degrading quietly. A member who types the URL gets the refusal
 * the API returned, because a capitalization view that renders empty for somebody who may not see it looks
 * exactly like a department that spent nothing.
 *
 * The money is shown beside the hours throughout, never instead of them. Where no rate card prices an hour the
 * money column is blank rather than zero — the difference between "we have not priced this" and "this was free"
 * is the whole reason the rate card is optional.
 */
@Component({
  selector: 'app-capex-opex',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, DecimalPipe, PageHeader],
  templateUrl: './capex-opex.html',
  styleUrl: './capex-opex.scss',
})
export class CapexOpex {
  private readonly directory = inject(DirectoryStore);
  private readonly preferences = inject(PreferencesStore);

  protected readonly finance = inject(FinanceStore);
  protected readonly projects = inject(ProjectsStore);

  protected readonly scopes: readonly FinanceScope[] = ['department', 'project', 'portfolio'];
  protected readonly periods = FINANCE_PERIODS;
  protected readonly buckets = BUCKETS;
  protected readonly treatments = TREATMENTS;

  protected readonly roles = computed(() => this.directory.allFunctionalRoles());

  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);

  /** The link the last export produced. Short-lived, which is why it is shown rather than followed silently. */
  protected readonly exportUrl = signal<string | null>(null);

  // --- The new rate card ---------------------------------------------------------------------------------------
  protected readonly cardRole = signal('');
  protected readonly cardRate = signal(0);
  protected readonly cardFrom = signal(zonedDay(new Date(), this.preferences.timeZone()));
  protected readonly cardTo = signal('');

  protected readonly showRules = signal(false);

  protected readonly awaitingProject = computed(
    () => this.finance.scope() === 'project' && !this.finance.scopeId(),
  );

  /** Whatever went wrong: an action's refusal, or the view itself failing to load. */
  protected readonly failure = computed(() => {
    const refused = this.message();

    if (refused) {
      return refused;
    }

    const error = this.finance.error() as
      { status?: number; error?: { detail?: string; title?: string } } | undefined;

    if (!error) {
      return null;
    }

    // A 403 here is the matrix, not a fault. Naming it lets the screen say "this is head-only" instead of
    // showing a raw problem detail somebody has to interpret.
    return error.status === 403
      ? 'finance.forbidden'
      : (error.error?.detail ?? error.error?.title ?? 'finance.loadFailed');
  });

  /** The bar widths, as percentages of the two columns together. Null where there is nothing to divide. */
  protected readonly share = computed(() => {
    const totals = this.finance.view()?.totals;

    if (!totals) {
      return null;
    }

    const total = totals.capexAmount + totals.opexAmount;

    return total === 0 ? null : { capex: (totals.capexAmount / total) * 100 };
  });

  protected setScope(scope: FinanceScope): void {
    this.finance.scope.set(scope);

    // A project id left over from the previous scope would be sent with a department request and quietly ignored,
    // which is the kind of thing that makes a screen look like it is showing the wrong period.
    if (scope !== 'project') {
      this.finance.scopeId.set(null);
    }
  }

  protected setPeriod(period: FinancePeriodKind): void {
    this.finance.period.set(period);
  }

  protected treatmentFor(bucket: Bucket): Treatment {
    const rule = this.finance.rule();

    return rule
      ? ({
          build: rule.buildTreatment,
          run: rule.runTreatment,
          qol: rule.qolTreatment,
          admin: rule.adminTreatment,
        }[bucket] as Treatment)
      : 'excluded';
  }

  protected async setTreatment(bucket: Bucket, treatment: Treatment): Promise<void> {
    const rule = this.finance.rule();

    if (!rule) {
      return;
    }

    await this.guard(() =>
      this.finance.saveRule({
        departmentId: rule.departmentId,
        buildTreatment: bucket === 'build' ? treatment : rule.buildTreatment,
        runTreatment: bucket === 'run' ? treatment : rule.runTreatment,
        qolTreatment: bucket === 'qol' ? treatment : rule.qolTreatment,
        adminTreatment: bucket === 'admin' ? treatment : rule.adminTreatment,
      }),
    );
  }

  protected async addRateCard(): Promise<void> {
    const rule = this.finance.rule();
    const role = this.cardRole();

    if (!rule || !role) {
      return;
    }

    await this.guard(async () => {
      await this.finance.saveRateCard({
        departmentId: rule.departmentId,
        functionalRoleId: role,
        hourlyRate: this.cardRate(),
        currency: this.finance.view()?.currency ?? 'EUR',
        effectiveFrom: this.cardFrom(),
        effectiveTo: this.cardTo() || null,
      });

      this.cardRole.set('');
      this.cardRate.set(0);
      this.cardTo.set('');
    });
  }

  protected async removeRateCard(card: RateCard): Promise<void> {
    await this.guard(() => this.finance.deleteRateCard(card.id));
  }

  protected async exportView(): Promise<void> {
    await this.guard(async () => {
      const exported = await this.finance.export();

      this.exportUrl.set(exported.url);
    });
  }

  private async guard(action: () => Promise<void>): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.message.set(null);

    try {
      await action();
    } catch (error) {
      const detail = (error as { error?: { detail?: string; title?: string } })?.error;

      this.message.set(detail?.detail ?? detail?.title ?? 'finance.saveFailed');
    } finally {
      this.busy.set(false);
    }
  }
}
