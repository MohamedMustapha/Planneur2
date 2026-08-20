/**
 * The left rail, in the order the design lays it out.
 *
 * `requiresAnyRole` hides an entry the viewer could not use. It is a tidiness measure, not a security boundary —
 * a determined user can type the URL, and what protects the data is the API policy plus RLS. Keeping that
 * distinction explicit is why the property is named for what it does to the *nav*, not for permission.
 */
export interface NavigationItem {
  readonly id: string;
  readonly route: string;
  /** Transloco key, resolved at render time so the rail re-labels on a language switch. */
  readonly labelKey: string;
  readonly icon: string;
  readonly requiresAnyRole?: readonly string[];
}

export const CONTEXTUAL_ROLES = {
  member: 'member',
  unitHead: 'unit-head',
  departmentHead: 'dept-head',
  projectLead: 'project-lead',
  productOwner: 'po',
  pmo: 'pmo',
} as const;

const HEADS = [CONTEXTUAL_ROLES.unitHead, CONTEXTUAL_ROLES.departmentHead, CONTEXTUAL_ROLES.pmo] as const;

export const NAVIGATION: readonly NavigationItem[] = [
  { id: 'board', route: '/board', labelKey: 'nav.myBoard', icon: '◧' },
  { id: 'team', route: '/team', labelKey: 'nav.myTeam', icon: '◫' },
  { id: 'unit', route: '/unit', labelKey: 'nav.myUnit', icon: '◨' },
  { id: 'department', route: '/department', labelKey: 'nav.department', icon: '▤', requiresAnyRole: HEADS },
  { id: 'projects', route: '/projects', labelKey: 'nav.projects', icon: '◈' },
  { id: 'portfolio', route: '/portfolio', labelKey: 'nav.portfolio', icon: '▦' },
  { id: 'reports', route: '/reports', labelKey: 'nav.reports', icon: '▥' },
  { id: 'kudos', route: '/kudos', labelKey: 'nav.kudos', icon: '★' },
  {
    id: 'finance',
    route: '/finance',
    labelKey: 'nav.finance',
    icon: '€',
    // visibility-matrix.md §4: capex/opex is visible to dept-head (their department) and PMO. Nobody else.
    requiresAnyRole: [CONTEXTUAL_ROLES.departmentHead, CONTEXTUAL_ROLES.pmo],
  },
  { id: 'settings', route: '/settings', labelKey: 'nav.settings', icon: '⚙', requiresAnyRole: HEADS },
];
