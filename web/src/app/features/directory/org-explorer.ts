import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  resource,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { DirectoryStore } from '../../core/directory/directory.store';
import { PersonSummary } from '../../core/directory/directory.models';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * How the org chart stacks the job titles it knows about.
 *
 * Titles only, and only the ones that describe a line of management. A tech lead outranks nobody — they lead a
 * design, not a team — so they are not here, and neither is anything a department invents for itself: an unknown
 * title sorts with the rest of the unit rather than being guessed at.
 */
const TITLE_RANK: Record<string, number> = {
  directeur: 0,
  'directeur-adjoint': 1,
  'chef-de-pole': 2,
};

/** Above this rank someone runs the department rather than a unit inside it. */
const LEADERSHIP_RANK = 1;

const UNRANKED = 9;

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

    return this.directory
      .units()
      .filter((unit) => !departmentId || unit.departmentId === departmentId);
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

  private readonly people = computed(() => this.peopleResource.value() ?? []);
  protected readonly isLoading = this.peopleResource.isLoading;

  protected readonly peopleCount = computed(() => this.people().length);

  /**
   * The directorate: whoever runs the department, and their deputy.
   *
   * Split out rather than sorted to the top of one list, because they are not the top of the unit — they are above
   * all of them. A flat table sorted by seniority says "most senior person in this unit"; two tables say "these
   * people run the department, and these people are in it", which is the shape the org actually has.
   */
  protected readonly leadership = computed(() =>
    this.ordered().filter((person) => this.rankOf(person) <= LEADERSHIP_RANK),
  );

  /** Everyone else, unit heads first — within a unit, that ordering is the hierarchy. */
  protected readonly members = computed(() =>
    this.ordered().filter((person) => this.rankOf(person) > LEADERSHIP_RANK),
  );

  /**
   * People sorted by standing, then by name.
   *
   * Rank comes from the functional role rather than from the contextual one, and deliberately: the directory is
   * an org chart, and what someone may read is a different question from where they sit. `/api/directory/people`
   * does not return contextual roles at all, which is the same decision taken one layer down.
   */
  private readonly ordered = computed(() =>
    [...this.people()].sort(
      (left, right) =>
        this.rankOf(left) - this.rankOf(right) || left.displayName.localeCompare(right.displayName),
    ),
  );

  protected readonly selectedUnitName = computed(() => {
    const id = this.selectedUnitId();

    return id ? (this.unitsInScope().find((unit) => unit.id === id)?.name ?? null) : null;
  });

  protected selectUnit(unitId: string | null): void {
    this.selectedUnitId.set(unitId);
  }

  /** The strongest title someone holds; unranked roles all tie at the bottom. */
  private rankOf(person: PersonSummary): number {
    return Math.min(
      ...person.functionalRoleCodes.map((code) => TITLE_RANK[code] ?? UNRANKED),
      UNRANKED,
    );
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
