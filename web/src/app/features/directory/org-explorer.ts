import { ChangeDetectionStrategy, Component, computed, inject, resource, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DirectoryStore } from '../../core/directory/directory.store';
import { PersonSummary } from '../../core/directory/directory.models';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * Department → unit → people.
 *
 * Read-only, and it does no filtering of its own: whatever `/api/directory/people` returns is what the caller is
 * allowed to see, because RLS decided that before the rows left Postgres. A member browsing here sees their whole
 * department — S1 widens the directory deliberately, since you cannot collaborate with colleagues you cannot find.
 */
@Component({
  selector: 'app-org-explorer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader],
  templateUrl: './org-explorer.html',
  styleUrl: './org-explorer.scss',
})
export class OrgExplorer {
  protected readonly directory = inject(DirectoryStore);
  protected readonly departments = inject(DepartmentScopeStore);

  protected readonly selectedUnitId = signal<string | null>(null);

  protected readonly unitsInScope = computed(() => {
    const departmentId = this.departments.selected()?.id;

    return this.directory.units().filter((unit) => !departmentId || unit.departmentId === departmentId);
  });

  private readonly peopleResource = resource({
    params: () => ({
      unitId: this.selectedUnitId(),
      departmentId: this.departments.selected()?.id ?? null,
    }),
    loader: async ({ params }): Promise<readonly PersonSummary[]> =>
      params.departmentId || params.unitId
        ? this.directory.people({
            unitId: params.unitId ?? undefined,
            departmentId: params.unitId ? undefined : (params.departmentId ?? undefined),
          })
        : [],
  });

  protected readonly people = computed(() => this.peopleResource.value() ?? []);
  protected readonly isLoading = this.peopleResource.isLoading;

  protected readonly selectedUnitName = computed(() => {
    const id = this.selectedUnitId();

    return id ? (this.unitsInScope().find((unit) => unit.id === id)?.name ?? null) : null;
  });

  protected selectUnit(unitId: string | null): void {
    this.selectedUnitId.set(unitId);
  }

  protected initials(person: PersonSummary): string {
    return person.displayName
      .split(/[\s.]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  }
}
