import { computed, Injectable, inject } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DepartmentConfig, DepartmentSummary, Me, PersonSummary, UnitSummary } from './directory.models';
import { SessionStore } from '../session/session.store';

/**
 * The directory, as signals.
 *
 * `me` is the client's starting context: who you are, which units and departments you belong to, and which
 * language your profile says you read. It replaces the hard-coded seed the shell used before S1 — the department
 * switcher, the org explorer and every board scope now come from the API, which means they come from RLS.
 */
@Injectable({ providedIn: 'root' })
export class DirectoryStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  /**
   * Only requested once authenticated. Asking earlier would produce a guaranteed 401 on every cold load, and an
   * error in the console that means nothing.
   */
  private readonly meResource = httpResource<Me>(() =>
    this.session.isAuthenticated() ? '/api/directory/me' : undefined,
  );

  private readonly departmentsResource = httpResource<DepartmentSummary[]>(() =>
    this.session.isAuthenticated() ? '/api/directory/departments' : undefined,
  );

  readonly me = computed(() => this.meResource.value());
  readonly isLoading = this.meResource.isLoading;

  /**
   * True when the caller authenticated but has no directory record — sync has not run, or ran and skipped them.
   * A real, actionable state that the shell surfaces rather than rendering an empty board with no explanation.
   */
  readonly isMissingFromDirectory = computed(() => {
    const error = this.meResource.error() as { status?: number } | undefined;

    return error?.status === 404;
  });

  readonly departments = computed<readonly DepartmentSummary[]>(() => this.departmentsResource.value() ?? []);
  readonly units = computed<readonly UnitSummary[]>(() => this.me()?.units ?? []);
  readonly functionalRoles = computed<readonly string[]>(() => this.me()?.functionalRoleCodes ?? []);
  readonly displayName = computed(() => this.me()?.displayName ?? this.session.displayName());

  async people(filter?: { unitId?: string; departmentId?: string }): Promise<readonly PersonSummary[]> {
    const params: Record<string, string> = {};

    if (filter?.unitId) {
      params['unitId'] = filter.unitId;
    }

    if (filter?.departmentId) {
      params['departmentId'] = filter.departmentId;
    }

    return firstValueFrom(this.http.get<PersonSummary[]>('/api/directory/people', { params }));
  }

  async config(departmentId: string): Promise<DepartmentConfig> {
    return firstValueFrom(this.http.get<DepartmentConfig>(`/api/directory/departments/${departmentId}/config`));
  }

  async saveConfig(departmentId: string, config: Omit<DepartmentConfig, 'departmentId' | 'version'>) {
    return firstValueFrom(
      this.http.put<DepartmentConfig>(`/api/directory/departments/${departmentId}/config`, config),
    );
  }

  reload(): void {
    this.meResource.reload();
    this.departmentsResource.reload();
  }
}
