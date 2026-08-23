import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  isMeasured,
  METRIC_KINDS,
  MetricKind,
  ObjectiveStatus,
  ObjectiveView,
  OBJECTIVE_STATUSES,
  StrategyStore,
} from '../../core/strategy/strategy.store';
import { SessionStore } from '../../core/session/session.store';
import { CONTEXTUAL_ROLES } from '../../core/navigation/navigation';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The strategy overview (v2 §06.5).
 *
 * Objectives as cards with a progress ring, not a table: a COPIL looks at this to decide what to talk about, and
 * the thing it needs to answer in one glance is "which of these is in trouble" — a question a grid of numbers
 * makes you read every row to answer. The alignment gaps sit on the same screen rather than behind a tab, because
 * an objective nobody is working on is exactly the kind of thing that never gets looked for on purpose.
 */
@Component({
  selector: 'app-strategy',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, DecimalPipe, PageHeader],
  templateUrl: './strategy.html',
  styleUrl: './strategy.scss',
})
export class Strategy {
  protected readonly store = inject(StrategyStore);
  private readonly session = inject(SessionStore);

  protected readonly metricKinds = METRIC_KINDS;
  protected readonly statuses = OBJECTIVE_STATUSES;

  protected readonly selected = signal<ObjectiveView | null>(null);
  protected readonly busy = signal(false);

  protected readonly objectiveFormOpen = signal(false);
  protected readonly draftTitle = signal('');
  protected readonly draftDescription = signal('');
  protected readonly draftMetric = signal<MetricKind>('number');
  protected readonly draftBaseline = signal<number | null>(null);
  protected readonly draftTarget = signal<number | null>(null);
  protected readonly draftUnit = signal('');
  protected readonly draftDue = signal('');
  protected readonly draftWeight = signal<number | null>(1);

  protected readonly reading = signal<number | null>(null);

  /** Editing is a head's or the PMO's. The button is hidden rather than shown and refused. */
  protected readonly canEdit = computed(() =>
    this.session
      .roles()
      .some((role) => role === CONTEXTUAL_ROLES.nodeHead || role === CONTEXTUAL_ROLES.pmo),
  );

  protected readonly canAdd = computed(
    () => this.draftTitle().trim().length > 3 && (!isMeasured(this.draftMetric()) || this.draftTarget() !== null),
  );

  protected readonly measured = computed(() => isMeasured(this.draftMetric()));

  /**
   * Where the ring's stroke stops. 0..1 in, circumference out.
   *
   * The circle is 2πr with r = 20, so a full ring is ~125.7. Kept as one function rather than inline arithmetic
   * in the template so the two rings on this screen can never drift apart by a rounding.
   */
  protected dash(progress: number): string {
    const circumference = 2 * Math.PI * 20;

    return `${(circumference * Math.max(0, Math.min(1, progress))).toFixed(2)} ${circumference.toFixed(2)}`;
  }

  protected pick(strategyId: string): void {
    this.store.selectedId.set(strategyId);
    this.selected.set(null);
  }

  protected select(objective: ObjectiveView): void {
    this.selected.set(objective);
    this.reading.set(objective.current);
  }

  protected close(): void {
    this.selected.set(null);
  }

  protected openObjectiveForm(): void {
    this.draftTitle.set('');
    this.draftDescription.set('');
    this.draftMetric.set('number');
    this.draftBaseline.set(null);
    this.draftTarget.set(null);
    this.draftUnit.set('');
    this.draftDue.set('');
    this.draftWeight.set(1);
    this.objectiveFormOpen.set(true);
  }

  protected closeObjectiveForm(): void {
    this.objectiveFormOpen.set(false);
  }

  protected async addObjective(): Promise<void> {
    const strategy = this.store.current();

    if (!strategy || !this.canAdd() || this.busy()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.store.addObjective(strategy.id, {
        title: this.draftTitle().trim(),
        description: this.draftDescription().trim() || null,
        metricKind: this.draftMetric(),
        baseline: this.draftBaseline(),
        target: this.draftTarget(),
        unit: this.draftUnit().trim() || null,
        due: this.draftDue() || null,
        weight: this.draftWeight(),
      });

      this.objectiveFormOpen.set(false);
    } finally {
      this.busy.set(false);
    }
  }

  protected async record(): Promise<void> {
    const objective = this.selected();
    const value = this.reading();

    if (!objective || value === null || this.busy()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.store.measure(objective.id, value);
      this.selected.set(
        this.store.objectives().find((candidate) => candidate.id === objective.id) ?? null,
      );
    } finally {
      this.busy.set(false);
    }
  }

  protected async override(status: string): Promise<void> {
    const objective = this.selected();

    if (!objective) {
      return;
    }

    await this.store.setStatus(objective.id, (status || null) as ObjectiveStatus | null);
    this.selected.set(
      this.store.objectives().find((candidate) => candidate.id === objective.id) ?? null,
    );
  }

  protected async unlink(objectiveId: string, contributionId: string): Promise<void> {
    await this.store.unlink(objectiveId, contributionId);
    this.selected.set(
      this.store.objectives().find((candidate) => candidate.id === objectiveId) ?? null,
    );
  }

  /** Links an unaligned item to the objective currently open. The one-click the alignment view promises. */
  protected async linkItem(itemId: string): Promise<void> {
    const objective = this.selected();

    if (!objective) {
      return;
    }

    await this.store.link(objective.id, 'item', itemId, null);
    this.selected.set(
      this.store.objectives().find((candidate) => candidate.id === objective.id) ?? null,
    );
  }
}
