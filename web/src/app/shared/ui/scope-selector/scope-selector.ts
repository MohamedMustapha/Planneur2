import { ChangeDetectionStrategy, Component, computed, inject, model } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { SessionStore } from '../../../core/session/session.store';

export type ReportScope = 'my-work' | 'my-project' | 'my-unit' | 'my-department' | 'portfolio';

interface ScopeOption {
  readonly id: ReportScope;
  readonly labelKey: string;
  /** Roles that unlock this scope. Mirrors visibility-matrix.md §6. */
  readonly roles: readonly string[] | null;
}

/**
 * Ordered narrowest to widest. The default selection is the *widest* the viewer's role grants, which is what the
 * matrix specifies: a department head opening a board should see their department, not have to climb to it.
 */
const SCOPES: readonly ScopeOption[] = [
  { id: 'my-work', labelKey: 'scope.myWork', roles: null },
  { id: 'my-project', labelKey: 'scope.myProject', roles: ['project-lead', 'po'] },
  { id: 'my-unit', labelKey: 'scope.myUnit', roles: ['node-head'] },
  { id: 'my-department', labelKey: 'scope.myDepartment', roles: ['node-head'] },
  { id: 'portfolio', labelKey: 'scope.portfolio', roles: ['pmo'] },
];

@Component({
  selector: 'app-scope-selector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective],
  templateUrl: './scope-selector.html',
  styleUrl: './scope-selector.scss',
})
export class ScopeSelector {
  private readonly session = inject(SessionStore);

  readonly scope = model<ReportScope | null>(null);

  protected readonly options = computed(() => {
    const roles = this.session.roles();

    return SCOPES.filter((option) => option.roles === null || option.roles.some((role) => roles.includes(role)));
  });

  /**
   * Falls back to the widest permitted scope until something sets one explicitly, so the control is never empty
   * and never shows a scope the viewer cannot use.
   */
  protected readonly effectiveScope = computed<ReportScope>(() => {
    const explicit = this.scope();
    const options = this.options();

    if (explicit && options.some((option) => option.id === explicit)) {
      return explicit;
    }

    return options.at(-1)?.id ?? 'my-work';
  });

  protected select(scope: ReportScope): void {
    this.scope.set(scope);
  }
}
