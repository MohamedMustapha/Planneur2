import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Mirrors the administration surface (v2 §08). */

export interface OrgLevelView {
  readonly levelNo: number;
  readonly code: string;
  readonly labelKey: string;
  readonly labelPluralKey: string;
  readonly headLabelKey: string;
  /** False means the level only ever holds other branches — nobody attaches there. */
  readonly peopleAllowed: boolean;
  readonly isOptional: boolean;
}

export interface OrgNodeAdminView {
  readonly id: string;
  readonly parentId: string | null;
  readonly levelNo: number;
  readonly code: string;
  readonly name: string;
  readonly headPersonId: string | null;
  readonly headName: string | null;
  readonly profileId: string | null;
  readonly active: boolean;
  /** The server walked the tree; the client indents by this rather than recursing over a shape it cannot bound. */
  readonly depth: number;
}

export interface AdminAuditDto {
  readonly id: string;
  readonly actorPersonId: string;
  readonly action: string;
  readonly targetType: string;
  readonly targetId: string;
  readonly nodeId: string | null;
  readonly detail: string;
  readonly occurredAt: string;
}

/**
 * The org structure and its trail, as signals.
 *
 * Reads are open to anybody — RLS is what decides whether the tree comes back with one branch or four hundred —
 * and every write goes through the same PATCH so a rename and a move cannot disagree about which node they meant.
 */
@Injectable({ providedIn: 'root' })
export class OrgAdminStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  readonly auditNodeId = signal<string | null>(null);

  private readonly levelsResource = httpResource<readonly OrgLevelView[]>(() =>
    this.session.isAuthenticated() ? '/api/admin/org/levels' : undefined,
  );

  private readonly nodesResource = httpResource<readonly OrgNodeAdminView[]>(() =>
    this.session.isAuthenticated() ? '/api/admin/org/nodes' : undefined,
  );

  private readonly auditResource = httpResource<readonly AdminAuditDto[]>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const node = this.auditNodeId();

    return node ? `/api/admin/audit?nodeId=${node}` : '/api/admin/audit';
  });

  readonly levels = computed<readonly OrgLevelView[]>(() => this.levelsResource.value() ?? []);
  readonly nodes = computed<readonly OrgNodeAdminView[]>(() => this.nodesResource.value() ?? []);
  readonly audit = computed<readonly AdminAuditDto[]>(() => this.auditResource.value() ?? []);

  readonly isLoading = computed(
    () => this.levelsResource.isLoading() || this.nodesResource.isLoading(),
  );

  reload(): void {
    this.levelsResource.reload();
    this.nodesResource.reload();
    this.auditResource.reload();
  }

  async saveLevel(level: OrgLevelView): Promise<void> {
    await firstValueFrom(this.http.put('/api/admin/org/levels', level));

    this.reload();
  }

  async createNode(payload: {
    parentId: string | null;
    levelNo: number;
    code: string;
    name: string;
  }): Promise<void> {
    await firstValueFrom(this.http.post('/api/admin/org/nodes', payload));

    this.reload();
  }

  /**
   * Amends a branch.
   *
   * `reparent` and `setHead` are flags rather than inferred from a null, because "move it to the top" and "leave
   * its parent alone" are both a null parent and only the caller knows which one they meant.
   */
  async amend(
    nodeId: string,
    patch: {
      name?: string;
      parentId?: string | null;
      reparent?: boolean;
      headPersonId?: string | null;
      setHead?: boolean;
      active?: boolean;
    },
  ): Promise<void> {
    await firstValueFrom(this.http.patch(`/api/admin/org/nodes/${nodeId}`, patch));

    this.reload();
  }
}
