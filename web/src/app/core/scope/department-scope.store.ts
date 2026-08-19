import { computed, Injectable, inject, signal } from '@angular/core';
import { DirectoryStore } from '../directory/directory.store';

export interface DepartmentScope {
  readonly id: string;
  /** Transloco key. Department names are localized, so the switcher renders a key, not stored text. */
  readonly nameKey: string;
  readonly code: string;
  readonly unitCount: number;
}

/**
 * Which department the boards are currently scoped to.
 *
 * The list comes from `/api/directory/departments`, which RLS has already filtered — a member gets their own
 * department, the PMO gets all of them, and neither the client nor this store had to know the difference. That is
 * the point: selecting a department narrows what the boards query and can never widen what the server returns.
 */
@Injectable({ providedIn: 'root' })
export class DepartmentScopeStore {
  private readonly directory = inject(DirectoryStore);

  private readonly explicitSelection = signal<string | null>(null);

  readonly available = computed<readonly DepartmentScope[]>(() => {
    const units = this.directory.units();

    return this.directory.departments().map((department) => ({
      id: department.id,
      nameKey: department.nameKey,
      code: department.code,
      unitCount: units.filter((unit) => unit.departmentId === department.id).length,
    }));
  });

  readonly selected = computed<DepartmentScope | null>(() => {
    const available = this.available();
    const explicit = this.explicitSelection();

    // Falls back to the caller's own department, then to the first they can see. An explicit choice that is no
    // longer in the list — they moved department, or lost a role — quietly stops applying rather than pinning the
    // shell to something the server will not return.
    return (
      available.find((department) => department.id === explicit) ??
      available.find((department) => department.id === this.directory.me()?.primaryDepartmentId) ??
      available[0] ??
      null
    );
  });

  readonly canSwitch = computed(() => this.available().length > 1);

  select(departmentId: string): void {
    this.explicitSelection.set(departmentId);
  }
}
