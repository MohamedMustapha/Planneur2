import { Routes } from '@angular/router';
import { authGuard } from './core/session/auth.guard';
import { landingGuard } from './core/navigation/landing.guard';

/**
 * One route per section the rail can draw, plus the detail routes those sections open.
 *
 * No route names a level: `/node` is the board at whatever depth the viewer sits, and which rows land in it is
 * the server's answer (v2 §00 §3, §02.1). Everything is lazy — the shell is the only thing in the initial bundle.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', canActivate: [authGuard, landingGuard], children: [] },
  {
    path: 'board',
    canActivate: [authGuard],
    loadComponent: () => import('./features/board/board').then((m) => m.Board),
  },
  {
    // The one board, parameterized by node: children aggregated where there are any, people where there are not.
    // `data` is load-bearing: withComponentInputBinding writes `undefined` into any input the route does not
    // name, so a board relying on its declared default arrives without one.
    path: 'node',
    canActivate: [authGuard],
    data: { board: 'node' },
    loadComponent: () => import('./features/scheduling/boards').then((m) => m.Boards),
  },
  {
    path: 'directory',
    canActivate: [authGuard],
    loadComponent: () => import('./features/directory/org-explorer').then((m) => m.OrgExplorer),
  },
  {
    // Superseded by the catalog as a rail entry, kept as a route: the portfolio board still links into a
    // project's detail, and a deep link that 404s is worse than a list nobody navigates to.
    path: 'projects',
    canActivate: [authGuard],
    loadComponent: () => import('./features/projects/project-list').then((m) => m.ProjectList),
  },
  {
    path: 'projects/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/projects/project-detail').then((m) => m.ProjectDetail),
  },
  {
    // The catalog is the landing view, and the flux board is one route away (v2 §03.2). The kanban answers
    // "where is our work"; the question people arrive with is "does this already exist".
    path: 'portfolio',
    canActivate: [authGuard],
    loadComponent: () => import('./features/portfolio/catalog').then((m) => m.Catalog),
  },
  {
    path: 'portfolio/flux',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/portfolio/portfolio-board').then((m) => m.PortfolioBoard),
  },
  {
    // The scope is not in the route: the report opens on the widest one the viewer's role grants, which only the
    // server knows.
    path: 'reports',
    canActivate: [authGuard],
    loadComponent: () => import('./features/reporting/report-view').then((m) => m.ReportView),
  },
  {
    path: 'kudos',
    canActivate: [authGuard],
    loadComponent: () => import('./features/kudos/kudos-wall').then((m) => m.KudosWall),
  },
  {
    // The consolidated view lands first (v2 §04.2) and always shows something; the per-item split is one route
    // along.
    path: 'finance',
    canActivate: [authGuard],
    loadComponent: () => import('./features/finance/consolidated').then((m) => m.Consolidated),
  },
  {
    path: 'finance/capex-opex',
    canActivate: [authGuard],
    loadComponent: () => import('./features/finance/capex-opex').then((m) => m.CapexOpex),
  },
  {
    // Read by anybody, written by heads. A strategy nobody below the head can open is a poster rather than a
    // spine (v2 §06).
    path: 'strategy',
    canActivate: [authGuard],
    loadComponent: () => import('./features/strategy/strategy').then((m) => m.Strategy),
  },
  {
    // Reporting an irritant needs no role, which is the whole point (v2 §05).
    path: 'problems',
    canActivate: [authGuard],
    loadComponent: () => import('./features/problems/problems').then((m) => m.Problems),
  },
  {
    // No role either. A CR is read by everybody the meeting reached and written by whoever ran it, and that
    // difference is a predicate rather than a route guard (v2 §07.6).
    path: 'meetings',
    canActivate: [authGuard],
    loadComponent: () => import('./features/meetings/meetings').then((m) => m.Meetings),
  },
  {
    path: 'settings',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/directory/department-settings').then((m) => m.DepartmentSettings),
  },
  {
    path: 'settings/meetings',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/meetings/meeting-manager').then((m) => m.MeetingManager),
  },
  {
    path: 'settings/integrations',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/integrations/integrations-admin').then((m) => m.IntegrationsAdmin),
  },
  {
    // Node profiles are the mechanism behind every other tab being editable rather than coded (v2 §10).
    path: 'settings/profiles',
    canActivate: [authGuard],
    loadComponent: () => import('./features/directory/node-profiles').then((m) => m.NodeProfiles),
  },
  {
    // How many levels there are, what they are called, and which branch hangs off which. Reachable by anybody —
    // RLS decides whether the tree comes back with one branch or four hundred (v2 §08.1).
    path: 'settings/org',
    canActivate: [authGuard],
    loadComponent: () => import('./features/admin/org-admin').then((m) => m.OrgAdmin),
  },
  {
    path: 'settings/access',
    canActivate: [authGuard],
    loadComponent: () => import('./features/access/rbac-admin').then((m) => m.RbacAdmin),
  },
  { path: '**', redirectTo: 'board' },
];
