import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Mirrors `Cracra.Modules.Integrations.Contracts`. */

export type ExternalProvider = 'azure-devops' | 'servicenow';

export type MappingKind = 'area-path' | 'iteration' | 'assignment-group';

export type SyncStatus = 'never' | 'ok' | 'failed';

export interface MappingView {
  readonly id: string;
  readonly kind: MappingKind;
  readonly externalValue: string;
  readonly projectId: string | null;
  readonly unitId: string | null;
}

export interface ConnectionView {
  readonly id: string;
  readonly departmentId: string;
  readonly provider: ExternalProvider;
  readonly name: string;
  readonly baseUrl: string;
  /** The name of a secret the deployment holds. Never the secret — it is not in the database to begin with. */
  readonly authRef: string;
  readonly projectOrQueue: string;
  readonly currentSprint: string | null;
  /** `hh:mm:ss`. Zero means on-demand only. */
  readonly pollInterval: string;
  readonly active: boolean;
  readonly lastSyncedAt: string | null;
  readonly lastSyncStatus: SyncStatus;
  readonly lastSyncError: string | null;
  readonly lastSyncItemCount: number;
  /** Open mirror rows this connection accounts for. "ok, 0 items" and "ok, 214" are very different situations. */
  readonly mirroredItemCount: number;
  readonly mappings: readonly MappingView[];
}

export interface ConnectionPayload {
  readonly departmentId: string;
  readonly provider: ExternalProvider;
  readonly name: string;
  readonly baseUrl: string;
  readonly authRef: string;
  readonly projectOrQueue: string;
  readonly currentSprint: string | null;
  readonly pollInterval: string;
  readonly active: boolean;
}

export interface MappingPayload {
  readonly kind: MappingKind;
  readonly externalValue: string;
  readonly projectId: string | null;
  readonly unitId: string | null;
}

export interface ExternalWorkItemView {
  readonly id: string;
  readonly provider: ExternalProvider;
  readonly externalId: string;
  readonly reference: string;
  readonly title: string;
  readonly type: string;
  readonly state: string;
  readonly sprintOrQueue: string | null;
  readonly projectId: string | null;
  readonly unitId: string | null;
  readonly assignedPersonId: string | null;
  readonly url: string | null;
  readonly estimatedHours: number | null;
  readonly updatedAtSource: string | null;
  readonly syncedAt: string;
  readonly mirrorState: 'open' | 'closed';
}

export const EXTERNAL_PROVIDERS: readonly ExternalProvider[] = ['azure-devops', 'servicenow'];

export const MAPPING_KINDS: readonly MappingKind[] = ['area-path', 'iteration', 'assignment-group'];

/** True where this kind maps onto a project; the rest map onto a unit. */
export function targetsProject(kind: MappingKind): boolean {
  return kind === 'area-path' || kind === 'iteration';
}

/**
 * Connections, their mappings, and what they have mirrored.
 *
 * One store for the administration screen and for anything that wants to look at the mirror, because they are the
 * same two endpoints. Note that nothing here can write a work item: the API exposes no such call, so the store
 * could not offer one even by accident — S10's read-only rule reaches this far by being an absence rather than a
 * convention.
 */
@Injectable({ providedIn: 'root' })
export class IntegrationsStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  /** Narrows the list to one department. Null asks for everything the caller may administer. */
  readonly departmentId = signal<string | null>(null);

  private readonly connectionsResource = httpResource<readonly ConnectionView[]>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const departmentId = this.departmentId();

    return departmentId
      ? `/api/integrations/connections?departmentId=${departmentId}`
      : '/api/integrations/connections';
  });

  readonly connections = computed<readonly ConnectionView[]>(
    () => this.connectionsResource.value() ?? [],
  );

  readonly isLoading = computed(() => this.connectionsResource.isLoading());

  /**
   * A list that failed to load is not the same as a deployment with no integrations.
   *
   * The first needs a message; the second is the ordinary state of most departments, and showing an error for it
   * would train people to ignore the error.
   */
  readonly loadError = computed(() => this.connectionsResource.error());

  async create(payload: ConnectionPayload): Promise<void> {
    await firstValueFrom(this.http.post('/api/integrations/connections', payload));
    this.reload();
  }

  async update(id: string, payload: ConnectionPayload): Promise<void> {
    await firstValueFrom(this.http.put(`/api/integrations/connections/${id}`, payload));
    this.reload();
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/integrations/connections/${id}`));
    this.reload();
  }

  async addMapping(connectionId: string, payload: MappingPayload): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/integrations/connections/${connectionId}/mappings`, payload),
    );

    this.reload();
  }

  async removeMapping(connectionId: string, mappingId: string): Promise<void> {
    await firstValueFrom(
      this.http.delete(`/api/integrations/connections/${connectionId}/mappings/${mappingId}`),
    );

    this.reload();
  }

  /**
   * Asks for a pull and returns as soon as it is accepted.
   *
   * The server answers 202: a pull is a round trip to somebody else's system, and the outcome arrives on the
   * connection's own last-sync fields rather than in this response. The screen reloads the list to show it.
   */
  async sync(connectionId: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/integrations/${connectionId}/sync`, {}));
  }

  /**
   * What one connection has mirrored, for the preview under it.
   *
   * By connection rather than by provider: an organization may have two DevOps connections, and a preview that
   * answered with both would be showing an administrator somebody else's queue under their card. Not a resource
   * either — the screen asks once when the panel opens, it does not watch.
   */
  async workItems(connectionId: string, limit = 10): Promise<readonly ExternalWorkItemView[]> {
    return firstValueFrom(
      this.http.get<ExternalWorkItemView[]>(
        `/api/integrations/work-items?connectionId=${connectionId}&limit=${limit}`,
      ),
    );
  }

  reload(): void {
    this.connectionsResource.reload();
  }
}
