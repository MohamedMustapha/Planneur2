import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { LocalizedNumber } from '../../core/i18n/localized-number.pipe';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  ProjectDetail as ProjectDetailModel,
  ProjectsStore,
  ProjectTeam,
  TeamCandidate,
} from '../../core/projects/projects.store';
import { AccessStore } from '../../core/access/access.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The project view.
 *
 * The team panel is the point of the screen: one collapsible group per contributing department, functional
 * sub-groups inside it, so "who from which department is doing what" is answerable at a glance. The grouping comes
 * from the API rather than being computed here, so the report (S8) and the timeline (S6) render the same answer
 * without reimplementing it.
 */
@Component({
  selector: 'app-project-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LocalizedNumber, TranslocoDirective, FormsModule, DecimalPipe, PageHeader],
  templateUrl: './project-detail.html',
  styleUrl: './project-detail.scss',
})
export class ProjectDetail {
  /** Bound from the route via withComponentInputBinding. */
  readonly id = input.required<string>();

  private readonly projects = inject(ProjectsStore);
  protected readonly access = inject(AccessStore);

  protected readonly project = signal<ProjectDetailModel | null>(null);
  protected readonly team = signal<ProjectTeam | null>(null);
  protected readonly candidates = signal<readonly TeamCandidate[]>([]);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly collapsed = signal<ReadonlySet<string>>(new Set());

  protected readonly selectedCandidate = signal<string>('');
  protected readonly selectedRole = signal<string>('');
  protected readonly allocation = signal<number | null>(null);

  /** Only a delivery lead sees the editing affordances. RLS still decides whether the write succeeds. */
  protected readonly canEdit = computed(() =>
    this.access.roles().some((role) => ['project-lead', 'po', 'node-head', 'pmo'].includes(role)),
  );

  protected readonly leadDepartment = computed(() =>
    this.project()?.departments.find((department) => department.isLead) ?? null,
  );

  constructor() {
    // input() is available synchronously in the constructor only via effect; a microtask keeps it simple and the
    // route parameter is stable for the component's lifetime.
    queueMicrotask(() => void this.load());
  }

  protected async load(): Promise<void> {
    try {
      const [project, team] = await Promise.all([
        this.projects.get(this.id()),
        this.projects.team(this.id()),
      ]);

      this.project.set(project);
      this.team.set(team);
      this.errorMessage.set(null);

      if (this.canEdit()) {
        this.candidates.set(await this.projects.candidates(this.id()));
      }
    } catch (error) {
      this.errorMessage.set(this.describe(error));
    }
  }

  protected toggle(departmentId: string): void {
    this.collapsed.update((current) => {
      const next = new Set(current);

      if (!next.delete(departmentId)) {
        next.add(departmentId);
      }

      return next;
    });
  }

  protected isCollapsed(departmentId: string): boolean {
    return this.collapsed().has(departmentId);
  }

  protected async addMember(): Promise<void> {
    const candidate = this.candidates().find((person) => person.personId === this.selectedCandidate());

    if (!candidate?.departmentId || !this.selectedRole()) {
      return;
    }

    try {
      await this.projects.addMember(this.id(), {
        personId: candidate.personId,
        departmentId: candidate.departmentId,
        functionalRoleId: this.selectedRole(),
        ...(this.allocation() ? { allocationPercent: this.allocation()! } : {}),
      });

      this.selectedCandidate.set('');
      this.allocation.set(null);

      await this.load();
      this.projects.reload();
    } catch (error) {
      this.errorMessage.set(this.describe(error));
    }
  }

  protected async removeMember(personId: string): Promise<void> {
    try {
      await this.projects.removeMember(this.id(), personId);

      await this.load();
      this.projects.reload();
    } catch (error) {
      this.errorMessage.set(this.describe(error));
    }
  }

  private describe(error: unknown): string {
    const problem = (error as { error?: { detail?: string; title?: string } }).error;

    return problem?.detail ?? problem?.title ?? 'projects.genericError';
  }

  protected initials(name: string | null): string {
    return (name ?? '?')
      .split(/[\s.]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  }
}
