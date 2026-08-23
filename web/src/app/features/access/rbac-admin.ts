import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { AccessStore, EffectiveRole, RbacOverrideItem } from '../../core/access/access.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { PersonSummary } from '../../core/directory/directory.models';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SettingsTabs } from '../directory/settings-tabs';

const GRANTABLE_ROLES = ['member', 'node-head', 'admin', 'project-lead', 'po', 'pmo'] as const;

/**
 * The RBAC fallback view: pick a person, see where each of their roles comes from, grant or deny one by hand.
 *
 * The source badge is the point of the screen. A role that came from an LDAP group and a role somebody granted
 * last Tuesday look identical in their effect and could not be more different in their meaning — one is the org
 * chart, the other is a decision a named person made and will have to justify.
 */
@Component({
  selector: 'app-rbac-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, DatePipe, PageHeader, SettingsTabs],
  templateUrl: './rbac-admin.html',
  styleUrl: './rbac-admin.scss',
})
export class RbacAdmin {
  private readonly access = inject(AccessStore);
  private readonly directory = inject(DirectoryStore);
  private readonly departments = inject(DepartmentScopeStore);

  protected readonly roles = GRANTABLE_ROLES;

  protected readonly people = signal<readonly PersonSummary[]>([]);
  protected readonly selected = signal<PersonSummary | null>(null);
  protected readonly effectiveRoles = signal<readonly EffectiveRole[]>([]);
  protected readonly overrides = signal<readonly RbacOverrideItem[]>([]);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly isBusy = signal(false);

  // The new-override form.
  protected readonly formRole = signal<string>('node-head');
  protected readonly formScopeType = signal<string>('Unit');
  protected readonly formIsGrant = signal(true);
  protected readonly formReason = signal('');
  protected readonly formExpiresAt = signal('');

  protected readonly search = signal('');

  protected readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();

    return term
      ? this.people().filter((person) => person.displayName.toLowerCase().includes(term))
      : this.people();
  });

  constructor() {
    void this.loadPeople();
  }

  protected async loadPeople(): Promise<void> {
    const departmentId = this.departments.selected()?.id;

    // No department filter of our own beyond the current scope: whatever the API returns is already everything
    // this head is allowed to administer.
    this.people.set(await this.directory.people(departmentId ? { departmentId } : undefined));
  }

  protected async select(person: PersonSummary): Promise<void> {
    this.selected.set(person);
    this.errorMessage.set(null);

    const [roles, overrides] = await Promise.all([
      this.access.rolesFor(person.id),
      this.access.overrides(person.id),
    ]);

    this.effectiveRoles.set(roles.roles);
    this.overrides.set(overrides);
  }

  /** ngValue keeps the option a real boolean; a plain [value] would arrive as the string "true". */
  protected setEffect(isGrant: boolean): void {
    this.formIsGrant.set(isGrant);
  }

  protected async submit(): Promise<void> {
    const person = this.selected();

    if (!person || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    this.errorMessage.set(null);

    try {
      await this.access.createOverride({
        personId: person.id,
        role: this.formRole(),
        scopeType: this.formScopeType(),
        scopeId: this.scopeIdFor(person, this.formScopeType()),
        isGrant: this.formIsGrant(),
        reason: this.formReason(),
        expiresAt: this.formExpiresAt() ? new Date(this.formExpiresAt()).toISOString() : null,
      });

      this.formReason.set('');
      this.formExpiresAt.set('');

      await this.select(person);
      this.access.reload();
    } catch (error) {
      this.errorMessage.set(this.describe(error));
    } finally {
      this.isBusy.set(false);
    }
  }

  protected async revoke(item: RbacOverrideItem): Promise<void> {
    const person = this.selected();

    if (!person) {
      return;
    }

    try {
      await this.access.revokeOverride(item.id);
      await this.select(person);
      this.access.reload();
    } catch (error) {
      this.errorMessage.set(this.describe(error));
    }
  }

  /**
   * A unit-scoped override applies to the person's own unit, a department-scoped one to their department. Global
   * takes no id. Asking the admin to paste a GUID would be the other option, and a mistyped one silently scopes
   * the grant to nothing.
   */
  private scopeIdFor(person: PersonSummary, scopeType: string): string | null {
    switch (scopeType) {
      case 'Unit':
        return person.unitId;
      case 'Department':
        return person.departmentId;
      default:
        return null;
    }
  }

  private describe(error: unknown): string {
    const problem = (error as { error?: { detail?: string; title?: string } }).error;

    return problem?.detail ?? problem?.title ?? 'access.genericError';
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
