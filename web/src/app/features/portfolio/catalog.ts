import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LocalizedNumber } from '../../core/i18n/localized-number.pipe';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  CatalogCard,
  CatalogItemDetail,
  CatalogStore,
  CLASSIFICATIONS,
  ItemClassification,
  ItemType,
  ITEM_TYPES,
  LifecycleState,
  LIFECYCLE_STATES,
} from '../../core/portfolio/catalog.store';
import { SessionStore } from '../../core/session/session.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The catalog: the default portfolio view (v2 §03.2).
 *
 * A grid of identity cards with facets, and a drawer for the full card. It replaces the empty kanban as the
 * landing view for one reason — the kanban answers "where is our work", and the question people actually arrive
 * with is "does this already exist". The flux board is still there, one tab away, for governance.
 */
@Component({
  selector: 'app-catalog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LocalizedNumber, TranslocoDirective, FormsModule, DecimalPipe, PageHeader],
  templateUrl: './catalog.html',
  styleUrl: './catalog.scss',
})
export class Catalog {
  protected readonly catalog = inject(CatalogStore);
  protected readonly session = inject(SessionStore);

  protected readonly types = ITEM_TYPES;
  protected readonly classifications = CLASSIFICATIONS;
  protected readonly states = LIFECYCLE_STATES;

  protected readonly selected = signal<CatalogItemDetail | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  // --- The drawer's editors (§03.6) ------------------------------------------------------------------------------

  protected readonly epicName = signal('');
  protected readonly epicStatus = signal<'idea' | 'planned' | 'deferred'>('planned');
  protected readonly epicVersion = signal('');
  protected readonly nextVersion = signal('');

  protected readonly iterationName = signal('');
  protected readonly iterationLength = signal('twoweeks');
  protected readonly iterationStart = signal('');

  protected readonly lengths: readonly string[] = ['oneweek', 'twoweeks', 'onemonth'];

  protected readonly dependencyQuery = signal('');
  protected readonly dependencyMatches = signal<readonly CatalogCard[]>([]);

  protected readonly epicStatuses: readonly ('idea' | 'planned' | 'deferred')[] = [
    'planned',
    'deferred',
    'idea',
  ];

  // --- The create wizard -----------------------------------------------------------------------------------------
  //
  // Four steps, and only the first two are required (§03.3). The v1 complaint was that you could not create a
  // project; a longer form would have been the wrong fix, so the wizard can be finished at step two and the rest
  // filled in on the card.

  protected readonly wizardOpen = signal(false);
  protected readonly step = signal(1);
  protected readonly draftName = signal('');
  protected readonly draftType = signal<ItemType>('project');
  protected readonly draftCategory = signal('');
  protected readonly draftClassification = signal<ItemClassification | ''>('');
  protected readonly draftSummary = signal('');
  protected readonly draftEstimate = signal<number | null>(null);

  /** Live duplicate check, so somebody sees the thing that already exists before they finish naming a new one. */
  protected readonly similar = signal<readonly CatalogCard[]>([]);

  protected readonly canAdvance = computed(() => this.draftName().trim().length > 1);

  protected readonly grouped = computed(() => {
    const cards = this.catalog.cards();

    return this.types
      .map((type) => ({ type, cards: cards.filter((card) => card.type === type) }))
      .filter((group) => group.cards.length > 0);
  });

  protected setType(value: string): void {
    this.catalog.setFacet('type', (value || null) as ItemType | null);
  }

  protected setClassification(value: string): void {
    this.catalog.setFacet('classification', (value || null) as ItemClassification | null);
  }

  protected setState(value: string): void {
    this.catalog.setFacet('state', (value || null) as LifecycleState | null);
  }

  protected setCategory(value: string): void {
    this.catalog.setFacet('category', value || null);
  }

  protected toggleShared(): void {
    this.catalog.setFacet('sharedOnly', !this.catalog.facets().sharedOnly);
  }

  protected async open(card: CatalogCard): Promise<void> {
    this.error.set(null);
    this.selected.set(await this.catalog.get(card.id));
  }

  protected close(): void {
    this.selected.set(null);
  }

  protected openWizard(): void {
    this.step.set(1);
    this.draftName.set('');
    this.draftType.set('project');
    this.draftCategory.set('');
    this.draftClassification.set('');
    this.draftSummary.set('');
    this.draftEstimate.set(null);
    this.similar.set([]);
    this.error.set(null);
    this.wizardOpen.set(true);
  }

  protected closeWizard(): void {
    this.wizardOpen.set(false);
  }

  protected async onNameChanged(name: string): Promise<void> {
    this.draftName.set(name);
    this.similar.set(await this.catalog.search(name));
  }

  protected async create(): Promise<void> {
    if (!this.canAdvance() || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const id = await this.catalog.create({
        name: this.draftName().trim(),
        type: this.draftType(),
        category: this.draftCategory().trim() || null,
        classification: this.draftClassification() || null,
        summary: this.draftSummary().trim() || null,
        estimateAmount: this.draftEstimate(),
      });

      this.wizardOpen.set(false);
      this.selected.set(await this.catalog.get(id));
    } catch {
      this.error.set('portfolio.catalog.createFailed');
    } finally {
      this.busy.set(false);
    }
  }

  /** The consumed / consumed-by split, from the same edges read at both ends. */
  protected consumes(detail: CatalogItemDetail): readonly ItemDependencyLike[] {
    return detail.dependencies.filter((edge) => edge.direction === 'consumes');
  }

  protected consumedBy(detail: CatalogItemDetail): readonly ItemDependencyLike[] {
    return detail.dependencies.filter((edge) => edge.direction === 'consumed-by');
  }

  /** Team grouped by the node each person contributes from, which is what §03.2 asks the drawer to show. */
  protected teamByNode(detail: CatalogItemDetail): readonly TeamGroup[] {
    const groups = new Map<string, TeamGroup>();

    for (const member of detail.team) {
      const existing = groups.get(member.nodeId);

      if (existing) {
        existing.members.push(member);
      } else {
        groups.set(member.nodeId, {
          nodeId: member.nodeId,
          nodeName: member.nodeName,
          members: [member],
        });
      }
    }

    return [...groups.values()];
  }

  protected async addIteration(): Promise<void> {
    const detail = this.selected();
    const name = this.iterationName().trim();
    const startsOn = this.iterationStart();

    if (!detail || name.length === 0 || startsOn.length === 0 || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.catalog.addIteration(detail.card.id, {
        name,
        length: this.iterationLength(),
        startsOn,
      });

      this.iterationName.set('');
      this.selected.set(await this.catalog.get(detail.card.id));
    } catch {
      this.error.set('portfolio.catalog.iterationFailed');
    } finally {
      this.busy.set(false);
    }
  }

  protected async closeIteration(iterationId: string): Promise<void> {
    const detail = this.selected();

    if (!detail || this.busy()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.catalog.closeIteration(detail.card.id, iterationId);

      this.selected.set(await this.catalog.get(detail.card.id));
    } finally {
      this.busy.set(false);
    }
  }

  protected async addEpic(): Promise<void> {
    const detail = this.selected();
    const name = this.epicName().trim();

    if (!detail || name.length === 0 || this.busy()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.catalog.addEpic(detail.card.id, name, this.epicStatus(), this.epicVersion().trim() || null);

      this.epicName.set('');
      this.selected.set(await this.catalog.get(detail.card.id));
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Declares the queued version. Offered only where something is already queued, because the server refuses it
   * otherwise — a button whose only outcome is a refusal teaches people to distrust the buttons.
   */
  protected async declareNextVersion(): Promise<void> {
    const detail = this.selected();
    const version = this.nextVersion().trim();

    if (!detail || version.length === 0 || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.catalog.awaitNextVersion(detail.card.id, version);

      this.nextVersion.set('');
      this.selected.set(await this.catalog.get(detail.card.id));
    } catch {
      this.error.set('portfolio.catalog.awaitFailed');
    } finally {
      this.busy.set(false);
    }
  }

  protected async searchDependency(query: string): Promise<void> {
    this.dependencyQuery.set(query);

    const detail = this.selected();
    const found = await this.catalog.search(query);

    // Never offer the item itself: the server refuses it, and a picker that lists an impossible choice is a
    // picker somebody will click.
    this.dependencyMatches.set(found.filter((card) => card.id !== detail?.card.id));
  }

  protected async addDependency(target: CatalogCard): Promise<void> {
    const detail = this.selected();

    if (!detail || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.catalog.addDependency(detail.card.id, target.id, 'consumes');

      this.dependencyQuery.set('');
      this.dependencyMatches.set([]);
      this.selected.set(await this.catalog.get(detail.card.id));
    } catch {
      this.error.set('portfolio.catalog.dependencyFailed');
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeDependency(dependencyId: string): Promise<void> {
    const detail = this.selected();

    if (!detail || this.busy()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.catalog.removeDependency(detail.card.id, dependencyId);

      this.selected.set(await this.catalog.get(detail.card.id));
    } finally {
      this.busy.set(false);
    }
  }

  protected canDeclareNext(detail: CatalogItemDetail): boolean {
    return detail.card.state === 'active' && this.queuedEpics(detail).length > 0;
  }

  protected queuedEpics(detail: CatalogItemDetail): readonly { name: string; status: string }[] {
    return detail.epics.filter(
      (epic) => epic.status === 'planned' || epic.status === 'deferred' || epic.status === 'in-progress',
    );
  }
}

interface ItemDependencyLike {
  readonly id: string;
  readonly itemCode: string;
  readonly itemName: string;
  readonly itemType: string;
  readonly kind: string;
}

interface TeamGroup {
  readonly nodeId: string;
  readonly nodeName: string | null;
  readonly members: {
    readonly personId: string;
    readonly displayName: string | null;
    readonly allocationPercent: number | null;
  }[];
}
