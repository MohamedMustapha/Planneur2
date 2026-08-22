/**
 * Mirrors `Cracra.Modules.Directory.Contracts`. One contract, two languages — a field renamed on either side
 * without the other is a runtime `undefined`, so keep them in step.
 */

export interface UnitSummary {
  readonly id: string;
  readonly departmentId: string;
  readonly code: string;
  readonly name: string;
  readonly kind: string;
}

export interface DepartmentSummary {
  readonly id: string;
  readonly code: string;
  /** Transloco key — departments are named in three languages, so the API returns a key, never display text. */
  readonly nameKey: string;
  readonly parentDepartmentId: string | null;
}

export interface PersonSummary {
  readonly id: string;
  readonly displayName: string;
  readonly unitId: string | null;
  readonly departmentId: string | null;
  readonly functionalRoleCodes: readonly string[];
  readonly active: boolean;
}

/** What `/api/directory/me` returns — the client's whole starting context. */
export interface Me {
  readonly personId: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly ldapUid: string;
  readonly primaryUnitId: string | null;
  readonly primaryDepartmentId: string | null;
  readonly timeZone: string;
  readonly uiLanguage: string;
  /** What the person chose for themselves. Null means the two synced values above still stand. */
  readonly preferredLanguage: string | null;
  readonly preferredTimeZone: string | null;
  readonly preferredTheme: string | null;
  /** Focus mode, as chosen by this person. Null means never chosen and the role default applies. */
  readonly focusMode: boolean | null;
  readonly units: readonly UnitSummary[];
  readonly departments: readonly DepartmentSummary[];
  readonly functionalRoleCodes: readonly string[];
  /**
   * For rendering only. The UI hides what you cannot use; RLS decides what you actually receive. If the two ever
   * disagree you see an empty table, never someone else's rows.
   */
  readonly contextualRoles: readonly string[];
}

/** One job identity, as the rate-card picker needs it: the id it is stored by, the key it renders through. */
export interface FunctionalRoleSummary {
  readonly id: string;
  readonly code: string;
  readonly labelKey: string;
  readonly departmentId: string | null;
}

export interface DepartmentConfig {
  readonly departmentId: string;
  readonly activityTaxonomyJson: string;
  readonly roleLabelsJson: string;
  readonly kudoRulesJson: string;
  readonly defaultBoardLayout: string;
  readonly iterationPresetsJson: string;
  readonly shiftTemplatesJson: string;
  readonly workingDayJson: string;
  readonly weeklyTargetHours: number;
  readonly enforceWeeklyTarget: boolean;
  readonly version: number;
}
