import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { zonedDay } from '../../core/time/zoned';
import {
  endDateFor,
  IterationLength,
  PortfolioItemDetail,
  PortfolioItemSummary,
  PortfolioState,
  PortfolioStore,
} from '../../core/portfolio/portfolio.store';
import { SessionStore } from '../../core/session/session.store';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/** The transition each lane offers. Null where the lane is terminal or the move is a PMO-only reversal. */
const NEXT_ACTION: Record<PortfolioState, 'commit' | 'activate' | 'archive' | null> = {
  considered: 'commit',
  committed: 'activate',
  active: 'archive',
  dephase: null,
};

/**
 * The portfolio board: four lanes, one card per item, and the iteration strip on the card that is running.
 *
 * The lanes and their order come from the server. The client never decides what a lane is — it renders what the
 * board says, so adding a state later is a server change, not a change in two places that must agree.
 */
@Component({
  selector: 'app-portfolio-board',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, DecimalPipe, RouterLink, PageHeader],
  templateUrl: './portfolio-board.html',
  styleUrl: './portfolio-board.scss',
})
export class PortfolioBoard {
  protected readonly portfolio = inject(PortfolioStore);
  private readonly preferences = inject(PreferencesStore);
  protected readonly session = inject(SessionStore);
  protected readonly departments = inject(DepartmentScopeStore);

  protected readonly lengths: readonly IterationLength[] = ['oneweek', 'twoweeks', 'onemonth', 'custom'];

  /**
   * The scope filter, alongside the department selector in the top bar.
   *
   * Only two entries, not the four the spec sketches: "my unit" and "my department" are already what the top bar's
   * department selector does, and offering the same narrowing twice in two places is how the two end up disagreeing.
   */
  protected readonly scopes = ['all', 'mine'] as const;

  protected readonly selected = signal<PortfolioItemDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** Which panel is open: the composer, a transition prompt, or the iteration form. */
  protected readonly composing = signal(false);
  protected readonly deciding = signal<PortfolioItemSummary | null>(null);
  protected readonly planning = signal<PortfolioItemSummary | null>(null);

  protected readonly candidateName = signal('');
  protected readonly candidatePriority = signal(100);
  protected readonly decisionNotes = signal('');
  protected readonly projectCode = signal('');

  protected readonly iterationName = signal('');
  protected readonly iterationLength = signal<IterationLength>('twoweeks');
  protected readonly iterationStart = signal(zonedDay(new Date(), this.preferences.timeZone()));
  protected readonly iterationEnd = signal('');

  /**
   * Registering a candidate and moving one are separate permissions on the server, and the rail shows the same
   * split: anyone who runs delivery can propose, only a head commits. Hiding a control the server would refuse is
   * a courtesy, not the enforcement — that is RLS's and the endpoint policies' job.
   */
  protected readonly canPropose = computed(() =>
    this.session.hasAny('project-lead', 'product-owner', 'unit-head', 'dept-head', 'pmo'),
  );

  protected readonly canDecide = computed(() => this.session.hasAny('unit-head', 'dept-head', 'pmo'));

  protected readonly canRevert = computed(() => this.session.has('pmo'));

  /** The preset's end date, shown live so the quick selector actually feels quick. */
  protected readonly impliedEnd = computed(() => {
    const length = this.iterationLength();

    return length === 'custom' ? this.iterationEnd() : endDateFor(length, this.iterationStart());
  });

  protected selectScope(scope: 'all' | 'mine'): void {
    this.portfolio.scope.set(scope);
  }

  protected actionFor(state: PortfolioState): string | null {
    return NEXT_ACTION[state];
  }

  protected async open(item: PortfolioItemSummary): Promise<void> {
    this.error.set(null);
    this.selected.set(await this.portfolio.get(item.id));
  }

  protected close(): void {
    this.selected.set(null);
    this.deciding.set(null);
    this.planning.set(null);
    this.composing.set(false);
  }

  protected startComposing(): void {
    this.candidateName.set('');
    this.candidatePriority.set(100);
    this.decisionNotes.set('');
    this.composing.set(true);
  }

  protected startDeciding(item: PortfolioItemSummary): void {
    this.decisionNotes.set('');
    this.projectCode.set('');
    this.deciding.set(item);
  }

  protected startPlanning(item: PortfolioItemSummary): void {
    this.iterationName.set('');
    this.iterationLength.set('twoweeks');
    this.iterationEnd.set('');
    this.planning.set(item);
  }

  protected async propose(): Promise<void> {
    const departmentId = this.departments.selected()?.id;

    if (!departmentId) {
      return;
    }

    await this.run(() =>
      this.portfolio.consider({
        name: this.candidateName(),
        departmentId,
        priority: this.candidatePriority(),
        notes: this.decisionNotes() || undefined,
      }),
    );

    this.composing.set(false);
  }

  protected async commit(): Promise<void> {
    const item = this.deciding();

    if (!item) {
      return;
    }

    await this.run(() =>
      this.portfolio.commit(item.id, {
        projectCode: this.projectCode(),
        projectName: item.name,
        decisionNotes: this.decisionNotes(),
      }),
    );

    this.deciding.set(null);
  }

  protected async activate(item: PortfolioItemSummary): Promise<void> {
    await this.run(() => this.portfolio.activate(item.id));
  }

  protected async archive(): Promise<void> {
    const item = this.deciding();

    if (!item) {
      return;
    }

    await this.run(() => this.portfolio.archive(item.id, this.decisionNotes()));

    this.deciding.set(null);
  }

  protected async plan(): Promise<void> {
    const item = this.planning();

    if (!item) {
      return;
    }

    await this.run(() =>
      this.portfolio.addIteration(item.id, {
        name: this.iterationName(),
        length: this.iterationLength(),
        startsOn: this.iterationStart(),
        // Only sent for a custom length; for a preset the server computes it, and its answer is the one that
        // matters if the two ever disagree.
        endsOn: this.iterationLength() === 'custom' ? this.iterationEnd() : undefined,
      }),
    );

    this.planning.set(null);
  }

  protected async closeIteration(item: PortfolioItemSummary): Promise<void> {
    if (!item.currentIteration) {
      return;
    }

    await this.run(() => this.portfolio.closeIteration(item.id, item.currentIteration!.id));
  }

  /**
   * Runs a transition and surfaces the refusal.
   *
   * A guard the server enforces will refuse from time to time — that is the point of it — and the person deserves
   * to be told which rule stopped them rather than watching the card not move.
   */
  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await action();

      const open = this.selected();

      if (open) {
        this.selected.set(await this.portfolio.get(open.item.id));
      }
    } catch (failure: unknown) {
      const detail = failure as { error?: { detail?: string; title?: string } };

      this.error.set(detail.error?.detail ?? detail.error?.title ?? 'portfolio.genericError');
    } finally {
      this.busy.set(false);
    }
  }
}
