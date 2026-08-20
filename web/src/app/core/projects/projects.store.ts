import { computed, Injectable, inject } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { DepartmentScopeStore } from '../scope/department-scope.store';

/** Mirrors `Cracra.Modules.Projects.Contracts`. */

export interface ProjectSummary {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  /** build | run | mixed — a stable code, colour-mapped by the design tokens. */
  readonly classification: string;
  readonly costAmount: number;
  readonly costCurrency: string;
  readonly leadDepartmentId: string;
  readonly ownerPersonId: string;
  readonly activeMemberCount: number;
}

export interface ProjectDepartmentSummary {
  readonly departmentId: string;
  readonly nameKey: string;
  readonly isLead: boolean;
}

export interface ProjectDetail extends Omit<ProjectSummary, 'activeMemberCount'> {
  readonly description: string | null;
  readonly costNotes: string | null;
  readonly departments: readonly ProjectDepartmentSummary[];
}

export interface ProjectTeamMember {
  readonly personId: string;
  /** Null when RLS hid the person: they still count, they are just not named. */
  readonly displayName: string | null;
  readonly allocationPercent: number | null;
  readonly from: string;
}

export interface ProjectTeamFunction {
  readonly functionalRoleId: string;
  readonly code: string;
  readonly members: readonly ProjectTeamMember[];
}

export interface ProjectTeamDepartment {
  readonly departmentId: string;
  readonly nameKey: string;
  readonly functions: readonly ProjectTeamFunction[];
}

export interface ProjectTeam {
  readonly projectId: string;
  readonly departments: readonly ProjectTeamDepartment[];
  readonly totalMembers: number;
}

export interface TeamCandidate {
  readonly personId: string;
  readonly displayName: string;
  readonly departmentId: string | null;
}

/**
 * Projects, as signals.
 *
 * The list is scoped to the department in the top bar, which narrows the query — it cannot widen it. Whatever RLS
 * decided the caller may see is the ceiling, and this store never filters below it in the client.
 */
@Injectable({ providedIn: 'root' })
export class ProjectsStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);
  private readonly departments = inject(DepartmentScopeStore);

  private readonly listResource = httpResource<ProjectSummary[]>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const departmentId = this.departments.selected()?.id;

    return departmentId ? `/api/projects?departmentId=${departmentId}` : '/api/projects';
  });

  readonly projects = computed<readonly ProjectSummary[]>(() => this.listResource.value() ?? []);
  readonly isLoading = this.listResource.isLoading;

  async get(projectId: string): Promise<ProjectDetail> {
    return firstValueFrom(this.http.get<ProjectDetail>(`/api/projects/${projectId}`));
  }

  async team(projectId: string): Promise<ProjectTeam> {
    return firstValueFrom(this.http.get<ProjectTeam>(`/api/projects/${projectId}/team`));
  }

  async candidates(projectId: string): Promise<readonly TeamCandidate[]> {
    return firstValueFrom(this.http.get<TeamCandidate[]>(`/api/projects/${projectId}/team/candidates`));
  }

  async addMember(
    projectId: string,
    member: { personId: string; departmentId: string; functionalRoleId: string; allocationPercent?: number },
  ): Promise<void> {
    await firstValueFrom(this.http.post(`/api/projects/${projectId}/members`, member));
  }

  async removeMember(projectId: string, personId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/projects/${projectId}/members/${personId}`));
  }

  reload(): void {
    this.listResource.reload();
  }
}
