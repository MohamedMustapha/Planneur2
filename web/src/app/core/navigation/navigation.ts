import { NODE_CAPABILITIES, NodeCapability } from '../directory/directory.models';

/** A section the shell can draw. Which ones, and where, is `/api/guidance/navigation`'s answer (v2 §02.1). */
export interface NavigationSection {
  readonly id: string;
  readonly route: string;
  readonly labelKey: string;
  readonly icon: string;
  readonly requiresCapability?: NodeCapability;
}

export const CONTEXTUAL_ROLES = {
  member: 'member',
  nodeHead: 'node-head',
  admin: 'admin',
  projectLead: 'project-lead',
  productOwner: 'po',
  pmo: 'pmo',
} as const;

export const SECTIONS: Readonly<Record<string, NavigationSection>> = {
  board: { id: 'board', route: '/board', labelKey: 'nav.myWeek', icon: '◧' },
  node: { id: 'node', route: '/node', labelKey: 'nav.myNode', icon: '◫' },
  portfolio: { id: 'portfolio', route: '/portfolio', labelKey: 'nav.portfolio', icon: '▦' },
  reports: { id: 'reports', route: '/reports', labelKey: 'nav.reports', icon: '▥' },
  strategy: {
    id: 'strategy',
    route: '/strategy',
    labelKey: 'nav.strategy',
    icon: '◎',
    requiresCapability: NODE_CAPABILITIES.strategy,
  },
  problems: { id: 'problems', route: '/problems', labelKey: 'nav.problems', icon: '⚑' },
  meetings: { id: 'meetings', route: '/meetings', labelKey: 'nav.meetings', icon: '◔' },
  kudos: {
    id: 'kudos',
    route: '/kudos',
    labelKey: 'nav.kudos',
    icon: '★',
    requiresCapability: NODE_CAPABILITIES.kudos,
  },
  finance: {
    id: 'finance',
    route: '/finance',
    labelKey: 'nav.finance',
    icon: '€',
    requiresCapability: NODE_CAPABILITIES.budget,
  },
  directory: { id: 'directory', route: '/directory', labelKey: 'nav.directory', icon: '◨' },
  admin: { id: 'admin', route: '/settings/org', labelKey: 'nav.settings', icon: '⚙' },
};

/** The shell before the server answers. A member's, because it is the narrowest thing to be wrong about. */
export const FALLBACK_NAVIGATION = {
  position: 'member',
  landingId: 'board',
  focusId: 'board',
  primary: ['board', 'problems', 'kudos'],
  secondary: ['node', 'portfolio', 'meetings', 'strategy', 'directory'],
} as const;

export function sectionsFor(ids: readonly string[]): readonly NavigationSection[] {
  return ids.map((id) => SECTIONS[id]).filter((section): section is NavigationSection => !!section);
}

export function routeFor(id: string): string {
  return SECTIONS[id]?.route ?? '/board';
}
