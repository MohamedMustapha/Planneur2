import { computed, Injectable, inject } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

export interface EffectiveRole {
  readonly role: string;
  readonly scopeType: string;
  readonly scopeId: string | null;
  /** `Ldap` or `RbacOverride` — the RBAC admin shows this so an override is never mistaken for a group. */
  readonly source: string;
}

export interface WhoAmI {
  readonly personId: string;
  readonly userName: string;
  readonly unitId: string | null;
  readonly departmentIds: readonly string[];
  readonly roles: readonly string[];
  readonly scopedRoles: readonly EffectiveRole[];
  readonly language: string;
}

export interface RbacOverrideItem {
  readonly id: string;
  readonly personId: string;
  readonly role: string;
  readonly scopeType: string;
  readonly scopeId: string | null;
  readonly isGrant: boolean;
  readonly reason: string;
  readonly expiresAt: string | null;
  readonly createdBy: string;
  readonly createdAt: string;
  readonly isActive: boolean;
}

export interface CreateOverride {
  readonly personId: string;
  readonly role: string;
  readonly scopeType: string;
  readonly scopeId: string | null;
  readonly isGrant: boolean;
  readonly reason: string;
  readonly expiresAt: string | null;
}

/**
 * The caller's effective roles, and the RBAC override admin.
 *
 * `whoami` is what the nav gates on. That gating is cosmetic: it decides which entries are drawn, never which rows
 * come back. A user who edits this response in their browser changes the menu and nothing else, because RLS
 * evaluates the same question again server-side on every query.
 */
@Injectable({ providedIn: 'root' })
export class AccessStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  private readonly whoamiResource = httpResource<WhoAmI>(() =>
    this.session.isAuthenticated() ? '/api/access/whoami' : undefined,
  );

  readonly whoami = computed(() => this.whoamiResource.value());

  /**
   * Effective roles, falling back to the session's while whoami is in flight — otherwise the rail would flicker
   * through a state where the user appears to have no roles at all.
   */
  readonly roles = computed<readonly string[]>(
    () => this.whoami()?.roles ?? this.session.roles(),
  );

  readonly scopedRoles = computed<readonly EffectiveRole[]>(() => this.whoami()?.scopedRoles ?? []);

  readonly isHead = computed(() =>
    this.roles().some((role) => role === 'unit-head' || role === 'dept-head' || role === 'pmo'),
  );

  has(role: string): boolean {
    return this.roles().includes(role);
  }

  async rolesFor(personId: string): Promise<{ roles: readonly EffectiveRole[]; roleNames: readonly string[] }> {
    return firstValueFrom(
      this.http.get<{ roles: EffectiveRole[]; roleNames: string[] }>(`/api/access/roles/${personId}`),
    );
  }

  async overrides(personId?: string): Promise<readonly RbacOverrideItem[]> {
    return firstValueFrom(
      this.http.get<RbacOverrideItem[]>(
        '/api/access/overrides',
        personId ? { params: { personId } } : {},
      ),
    );
  }

  async createOverride(request: CreateOverride): Promise<RbacOverrideItem> {
    return firstValueFrom(this.http.post<RbacOverrideItem>('/api/access/overrides', request));
  }

  async revokeOverride(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/access/overrides/${id}`));
  }

  reload(): void {
    this.whoamiResource.reload();
  }
}
