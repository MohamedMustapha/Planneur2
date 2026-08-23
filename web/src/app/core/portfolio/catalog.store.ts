import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Mirrors the catalog half of `Cracra.Modules.Portfolio.Contracts` (v2 §03). */

export type ItemType =
  | 'project'
  | 'platform'
  | 'product'
  | 'run-service'
  | 'business-initiative'
  | 'intelligence';

export type ItemClassification = 'build' | 'run' | 'mixed';

export type LifecycleState = 'considered' | 'committed' | 'active' | 'awaiting-vnext' | 'dephase';

export const ITEM_TYPES: readonly ItemType[] = [
  'project',
  'platform',
  'product',
  'run-service',
  'business-initiative',
  'intelligence',
];

export const CLASSIFICATIONS: readonly ItemClassification[] = ['build', 'run', 'mixed'];

export const LIFECYCLE_STATES: readonly LifecycleState[] = [
  'considered',
  'committed',
  'active',
  'awaiting-vnext',
  'dephase',
];

export interface CatalogCard {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly type: ItemType;
  readonly category: string | null;
  readonly classification: ItemClassification;
  readonly state: LifecycleState;
  readonly awaitingVersion: string | null;
  readonly ownerNodeId: string;
  readonly leadPersonId: string | null;
  readonly poPersonId: string | null;
  readonly summary: string | null;
  readonly estimateAmount: number | null;
  readonly currency: string;
  readonly teamHeadcount: number;
  readonly dependencyCount: number;
  readonly consumedByCount: number;
  readonly queuedEpicCount: number;
  readonly currentIterationName: string | null;
  readonly confidential: boolean;
}

export interface ItemTeamMember {
  readonly personId: string;
  readonly displayName: string | null;
  readonly nodeId: string;
  readonly nodeName: string | null;
  readonly functionalRoleId: string | null;
  readonly allocationPercent: number | null;
  readonly from: string;
  readonly to: string | null;
}

export interface ItemEpicView {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly status: 'idea' | 'planned' | 'in-progress' | 'done' | 'deferred';
  readonly targetVersion: string | null;
  readonly iterationId: string | null;
  readonly sequence: number;
}

export interface ItemDependencyView {
  readonly id: string;
  readonly itemId: string;
  readonly itemCode: string;
  readonly itemName: string;
  readonly itemType: ItemType;
  readonly kind: 'consumes' | 'integrates' | 'blocks';
  readonly direction: 'consumes' | 'consumed-by';
  readonly note: string | null;
}

export interface CatalogItemDetail {
  readonly card: CatalogCard;
  readonly team: readonly ItemTeamMember[];
  readonly iterations: readonly { readonly id: string; readonly name: string; readonly state: string }[];
  readonly epics: readonly ItemEpicView[];
  readonly dependencies: readonly ItemDependencyView[];
  readonly history: readonly { readonly toState: string; readonly reason: string }[];
}

export interface CatalogFacets {
  readonly type: ItemType | null;
  readonly category: string | null;
  readonly classification: ItemClassification | null;
  readonly state: LifecycleState | null;
  readonly sharedOnly: boolean;
}

export interface CreateItemPayload {
  readonly name: string;
  readonly type: ItemType;
  readonly category?: string | null;
  readonly classification?: ItemClassification | null;
  readonly ownerNodeId?: string | null;
  readonly summary?: string | null;
  readonly estimateAmount?: number | null;
}

/**
 * The catalog, as signals.
 *
 * The facets live here rather than in the page because the create wizard and the dependency picker both read the
 * same list, and two copies of "what is currently filtered" is how a card disappears from one view and not the
 * other.
 */
@Injectable({ providedIn: 'root' })
export class CatalogStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  readonly facets = signal<CatalogFacets>({
    type: null,
    category: null,
    classification: null,
    state: null,
    sharedOnly: false,
  });

  private readonly cardsResource = httpResource<readonly CatalogCard[]>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const facets = this.facets();
    const params = new URLSearchParams();

    if (facets.type) {
      params.set('type', facets.type);
    }

    if (facets.category) {
      params.set('category', facets.category);
    }

    if (facets.classification) {
      params.set('classification', facets.classification);
    }

    if (facets.state) {
      params.set('state', facets.state);
    }

    if (facets.sharedOnly) {
      params.set('sharedOnly', 'true');
    }

    const query = params.toString();

    return query ? `/api/portfolio/catalog?${query}` : '/api/portfolio/catalog';
  });

  readonly cards = computed<readonly CatalogCard[]>(() => this.cardsResource.value() ?? []);
  readonly isLoading = this.cardsResource.isLoading;
  readonly total = computed(() => this.cards().length);

  /** The categories actually in use, so the facet offers the deployment's own words and not a fixed list. */
  readonly categories = computed<readonly string[]>(() =>
    [...new Set(this.cards().map((card) => card.category).filter((c): c is string => !!c))].sort(),
  );

  setFacet<K extends keyof CatalogFacets>(key: K, value: CatalogFacets[K]): void {
    this.facets.update((facets) => ({ ...facets, [key]: value }));
  }

  clearFacets(): void {
    this.facets.set({
      type: null,
      category: null,
      classification: null,
      state: null,
      sharedOnly: false,
    });
  }

  refresh(): void {
    this.cardsResource.reload();
  }

  async get(itemId: string): Promise<CatalogItemDetail> {
    return firstValueFrom(this.http.get<CatalogItemDetail>(`/api/portfolio/items/${itemId}`));
  }

  /** The "does something similar already exist" search. Empty for anything too short to be a real term. */
  async search(query: string): Promise<readonly CatalogCard[]> {
    const trimmed = query.trim();

    if (trimmed.length < 2) {
      return [];
    }

    return firstValueFrom(
      this.http.get<readonly CatalogCard[]>(
        `/api/portfolio/catalog/search?q=${encodeURIComponent(trimmed)}`,
      ),
    );
  }

  async create(payload: CreateItemPayload): Promise<string> {
    const created = await firstValueFrom(
      this.http.post<{ id: string }>('/api/portfolio/items', payload),
    );

    this.refresh();

    return created.id;
  }

  async addDependency(itemId: string, dependsOnItemId: string, kind: string): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/portfolio/items/${itemId}/dependencies`, { dependsOnItemId, kind }),
    );
  }

  async removeDependency(itemId: string, dependencyId: string): Promise<void> {
    await firstValueFrom(
      this.http.delete(`/api/portfolio/items/${itemId}/dependencies/${dependencyId}`),
    );
  }

  async addEpic(itemId: string, name: string, status: string, targetVersion: string | null): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/portfolio/${itemId}/epics`, { name, status, targetVersion }),
    );
  }

  async awaitNextVersion(itemId: string, version: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/portfolio/${itemId}/await-next`, { version }));

    this.refresh();
  }
}
