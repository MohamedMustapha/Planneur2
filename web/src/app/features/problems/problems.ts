import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  FREQUENCIES,
  ImpactFrequency,
  ProblemCard,
  ProblemCategory,
  ProblemDetail,
  ProblemSort,
  PROBLEM_CATEGORIES,
  ProblemsStore,
} from '../../core/problems/problems.store';
import { SessionStore } from '../../core/session/session.store';
import { CONTEXTUAL_ROLES } from '../../core/navigation/navigation';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The problems board (v2 §05.5).
 *
 * A ranked list rather than a kanban by default, because the question the board answers is "what is costing us
 * most", and status columns answer "where is it in the process" — which matters to the handful of people who
 * triage and to nobody else.
 */
@Component({
  selector: 'app-problems',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, DecimalPipe, PageHeader],
  templateUrl: './problems.html',
  styleUrl: './problems.scss',
})
export class Problems {
  protected readonly problems = inject(ProblemsStore);
  private readonly session = inject(SessionStore);

  protected readonly categories = PROBLEM_CATEGORIES;
  protected readonly frequencies = FREQUENCIES;
  protected readonly sorts: readonly ProblemSort[] = ['impact', 'votes', 'recent'];

  protected readonly selected = signal<ProblemDetail | null>(null);
  protected readonly busy = signal(false);

  protected readonly formOpen = signal(false);
  protected readonly draftTitle = signal('');
  protected readonly draftDescription = signal('');
  protected readonly draftCategory = signal<ProblemCategory>('work-process');
  protected readonly draftTimeLoss = signal<number | null>(null);
  protected readonly draftFrequency = signal<ImpactFrequency>('weekly');
  protected readonly draftAffected = signal<number | null>(null);

  /** Live duplicate hint. Never blocks the send — the point is to be seen, not to argue. */
  protected readonly similar = signal<readonly ProblemCard[]>([]);

  protected readonly canSend = computed(() => this.draftTitle().trim().length > 3);

  /** Triage is a head's act, and the button is hidden rather than shown-and-refused. */
  protected readonly canTriage = computed(() =>
    this.session.roles().some((role) => role === CONTEXTUAL_ROLES.nodeHead || role === CONTEXTUAL_ROLES.pmo),
  );

  protected setSort(value: string): void {
    this.problems.sort.set(value as ProblemSort);
  }

  protected setCategory(value: string): void {
    this.problems.category.set((value || null) as ProblemCategory | null);
  }

  protected openForm(): void {
    this.draftTitle.set('');
    this.draftDescription.set('');
    this.draftCategory.set('work-process');
    this.draftTimeLoss.set(null);
    this.draftFrequency.set('weekly');
    this.draftAffected.set(null);
    this.similar.set([]);
    this.formOpen.set(true);
  }

  protected closeForm(): void {
    this.formOpen.set(false);
  }

  protected async onTitleChanged(title: string): Promise<void> {
    this.draftTitle.set(title);
    this.similar.set(await this.problems.search(title));
  }

  protected async send(): Promise<void> {
    if (!this.canSend() || this.busy()) {
      return;
    }

    this.busy.set(true);

    try {
      const filed = await this.problems.file({
        title: this.draftTitle().trim(),
        description: this.draftDescription().trim() || null,
        category: this.draftCategory(),
        impactTimeLoss: this.draftTimeLoss(),
        impactFrequency: this.draftFrequency(),
        affectedPeopleEstimate: this.draftAffected(),
      });

      this.formOpen.set(false);
      this.selected.set(await this.problems.get(filed.id));
    } finally {
      this.busy.set(false);
    }
  }

  protected async open(card: ProblemCard): Promise<void> {
    this.selected.set(await this.problems.get(card.id));
  }

  protected close(): void {
    this.selected.set(null);
  }

  protected async vote(card: ProblemCard): Promise<void> {
    await this.problems.vote(card.id);

    if (this.selected()?.card.id === card.id) {
      this.selected.set(await this.problems.get(card.id));
    }
  }

  protected async accept(card: ProblemCard): Promise<void> {
    await this.problems.triage(card.id, 'accepted', null);
    this.selected.set(await this.problems.get(card.id));
  }

  protected async convert(card: ProblemCard): Promise<void> {
    await this.problems.convert(card.id, 'project');
    this.selected.set(await this.problems.get(card.id));
  }
}
