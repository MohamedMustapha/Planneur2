import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * A profile as the admin screen edits it — nulls preserved.
 *
 * Mirrors `NodeProfileDetail`, not `NodeProfileSnapshot`. The distinction matters more here than anywhere else in
 * the client: null means "inherit from above", so a form that helpfully substituted the resolved value would turn
 * every save into a full copy of the parent and quietly stop the child tracking it.
 */
export interface NodeProfileDetail {
  readonly id: string;
  readonly code: string;
  readonly labelKey: string;
  readonly activityTaxonomyJson: string | null;
  readonly boardArchetypes: readonly string[] | null;
  readonly itemTypes: readonly string[] | null;
  readonly capabilitiesJson: string | null;
  readonly solvesCategories: readonly string[] | null;
  readonly budgetDefaultsJson: string | null;
  readonly headlinePattern: string | null;
}

export type SaveNodeProfile = Omit<NodeProfileDetail, 'id'>;

/** Authoring and attaching node profiles (v2 §10.6). */
@Injectable({ providedIn: 'root' })
export class NodeProfileStore {
  private readonly http = inject(HttpClient);

  async list(): Promise<readonly NodeProfileDetail[]> {
    return firstValueFrom(this.http.get<NodeProfileDetail[]>('/api/directory/profiles'));
  }

  /**
   * Every capability the platform knows, with its default.
   *
   * Fetched rather than hardcoded in the form so a capability added on the server appears here without anyone
   * remembering to add a checkbox — which is the registry rule from §10.6 applied to the authoring screen itself.
   */
  async capabilities(): Promise<Readonly<Record<string, boolean>>> {
    return firstValueFrom(
      this.http.get<Record<string, boolean>>('/api/directory/profiles/capabilities'),
    );
  }

  async create(profile: SaveNodeProfile): Promise<NodeProfileDetail> {
    return firstValueFrom(this.http.post<NodeProfileDetail>('/api/directory/profiles', profile));
  }

  /** The expected authoring path per §10.6 — a new branch starts from a close profile and edits it. */
  async clone(id: string, code: string, labelKey: string): Promise<NodeProfileDetail> {
    return firstValueFrom(
      this.http.post<NodeProfileDetail>(`/api/directory/profiles/${id}/clone`, { code, labelKey }),
    );
  }

  async update(id: string, profile: SaveNodeProfile): Promise<NodeProfileDetail> {
    return firstValueFrom(
      this.http.put<NodeProfileDetail>(`/api/directory/profiles/${id}`, profile),
    );
  }

  /** `profileId: null` detaches, so the unit inherits from its department again. */
  async attachToUnit(unitId: string, profileId: string | null): Promise<void> {
    await firstValueFrom(this.http.put<void>(`/api/directory/units/${unitId}/profile`, { profileId }));
  }

  async attachToDepartment(departmentId: string, profileId: string | null): Promise<void> {
    await firstValueFrom(
      this.http.put<void>(`/api/directory/departments/${departmentId}/profile`, { profileId }),
    );
  }
}
