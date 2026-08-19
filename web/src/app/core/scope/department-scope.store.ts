import { computed, Injectable, inject, signal } from '@angular/core';
import { SessionStore } from '../session/session.store';

export interface DepartmentScope {
  readonly id: string;
  readonly name: string;
  readonly unitCount: number;
}

/**
 * The seeded organisation, matching `deploy/keycloak/build-realm.py` id for id.
 *
 * S1 replaces this with the Directory module's `/api/directory/departments`. It lives here rather than being
 * faked inside the top bar so that replacement is a one-line change to this store and nothing else — the shell
 * already consumes it through the same signals the real thing will expose.
 */
const SEEDED_DEPARTMENTS: readonly DepartmentScope[] = [
  { id: '11111111-1111-1111-1111-111111111111', name: "Direction des Systèmes d'Information", unitCount: 2 },
  { id: '22222222-2222-2222-2222-222222222222', name: 'Direction Financière', unitCount: 2 },
];

/**
 * Which department the boards are currently scoped to.
 *
 * A viewer only ever sees the departments their token grants — PMO sees all of them, everyone else sees their own.
 * Selecting one narrows the query; it can never widen what the server will return.
 */
@Injectable({ providedIn: 'root' })
export class DepartmentScopeStore {
  private readonly session = inject(SessionStore);

  private readonly explicitSelection = signal<string | null>(null);

  readonly available = computed<readonly DepartmentScope[]>(() => {
    const user = this.session.user();

    if (user.roles.includes('pmo')) {
      return SEEDED_DEPARTMENTS;
    }

    const mine = SEEDED_DEPARTMENTS.filter((department) => user.departmentIds.includes(department.id));

    return mine.length > 0 ? mine : SEEDED_DEPARTMENTS.slice(0, 1);
  });

  readonly selected = computed<DepartmentScope | null>(() => {
    const available = this.available();
    const explicit = this.explicitSelection();

    return available.find((department) => department.id === explicit) ?? available[0] ?? null;
  });

  readonly canSwitch = computed(() => this.available().length > 1);

  select(departmentId: string): void {
    this.explicitSelection.set(departmentId);
  }
}
