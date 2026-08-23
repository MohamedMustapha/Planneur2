import { Routes } from '@angular/router';
import { authGuard } from './core/session/auth.guard';

/**
 * One route per rail entry, so the shell was fully navigable from S0 and each slice replaced a placeholder rather
 * than adding a route. Everything is lazy: the shell is the only thing in the initial bundle.
 *
 * S11 was the last placeholder, so the helper that produced them — and the screen it loaded — are gone. Keeping
 * them would leave a component nothing routes to and a set of translations describing screens that now exist.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'board' },
  {
    path: 'board',
    canActivate: [authGuard],
    loadComponent: () => import('./features/board/board').then((m) => m.Board),
  },
  {
    // The five boards are one screen with a tab strip, not five routes: the scope selector and the week pager are
    // the same controls throughout, and splitting them would mean five copies that drift. The rail entries still
    // land on the board they name, which is what `board` in the route data does.
    path: 'team',
    canActivate: [authGuard],
    data: { board: 'team' },
    loadComponent: () => import('./features/scheduling/boards').then((m) => m.Boards),
  },
  {
    // S1 delivers the org explorer; the timeline view of a unit still belongs to S6.
    path: 'unit',
    canActivate: [authGuard],
    loadComponent: () => import('./features/directory/org-explorer').then((m) => m.OrgExplorer),
  },
  {
    path: 'department',
    canActivate: [authGuard],
    data: { board: 'department' },
    loadComponent: () => import('./features/scheduling/boards').then((m) => m.Boards),
  },
  {
    path: 'projects',
    canActivate: [authGuard],
    loadComponent: () => import('./features/projects/project-list').then((m) => m.ProjectList),
  },
  {
    // withComponentInputBinding maps the route parameter straight onto the component's id input.
    path: 'projects/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./features/projects/project-detail').then((m) => m.ProjectDetail),
  },
  {
    // The catalog is the landing view now, and the flux board is one route away (v2 §03.2). The kanban answers
    // "where is our work"; the question people arrive with is "does this already exist", and only one of those
    // two is worth an empty screen when the answer is no.
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
    // server knows. A /reports/department URL would be a client-side claim about the visibility matrix.
    path: 'reports',
    canActivate: [authGuard],
    loadComponent: () => import('./features/reporting/report-view').then((m) => m.ReportView),
  },
  {
    // No scope in the route, for the same reason the report has none: which unit or department somebody's
    // recognition sits in is a server fact, and a /kudos/unit/<id> URL would be a client-side claim about it.
    path: 'kudos',
    canActivate: [authGuard],
    loadComponent: () => import('./features/kudos/kudos-wall').then((m) => m.KudosWall),
  },
  {
    // No scope in the route, for the same reason the report has none: which department's capitalization somebody
    // may read is a server fact, and a /finance/department/<id> URL would be a client-side claim about it.
    // The consolidated view lands first (v2 §04.2): it opens on the highest node the caller heads and always
    // shows something, where the capex/opex screen needed a project picked before it said anything at all. That
    // one is still here, one route along, for the per-project split.
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
    // spine, and the whole slice exists to make "what does my work serve" answerable (v2 §06).
    path: 'strategy',
    canActivate: [authGuard],
    loadComponent: () => import('./features/strategy/strategy').then((m) => m.Strategy),
  },
  {
    // Reporting an irritant needs no role, which is the whole point: a platform where saying "this wastes my
    // week" requires a hat is a platform where nobody says it (v2 §05).
    path: 'problems',
    canActivate: [authGuard],
    loadComponent: () => import('./features/problems/problems').then((m) => m.Problems),
  },
  {
    path: 'settings',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/directory/department-settings').then((m) => m.DepartmentSettings),
  },
  {
    // Third tab of the same administration screen. Recurring meetings and special days are configuration of a
    // scope, which is what this section is for; everybody else meets the same data on the boards and in the
    // dashboard's "coming up" strip.
    path: 'settings/meetings',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/meetings/meeting-manager').then((m) => m.MeetingManager),
  },
  {
    // Fourth tab of the same administration screen. Which DevOps project or ServiceNow queue a department pulls
    // from is configuration of that department, and it is the department head who owns the relationship — so it
    // sits beside the other things they configure rather than under a platform-admin section nobody has.
    path: 'settings/integrations',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/integrations/integrations-admin').then((m) => m.IntegrationsAdmin),
  },
  {
    // Fifth tab. Node profiles are the mechanism behind every other tab being editable rather than coded (v2
    // §10), so they belong with the rest of the configuration an administrator owns — and the route stays
    // reachable even where the integrations tab is hidden, because a branch without integrations still has a
    // profile that says so.
    path: 'settings/profiles',
    canActivate: [authGuard],
    loadComponent: () => import('./features/directory/node-profiles').then((m) => m.NodeProfiles),
  },
  {
    // Behind the same nav entry as department settings: the rail is fixed at ten sections by the design, and
    // both screens are the same job from an administrator's point of view.
    path: 'settings/access',
    canActivate: [authGuard],
    loadComponent: () => import('./features/access/rbac-admin').then((m) => m.RbacAdmin),
  },
  { path: '**', redirectTo: 'board' },
];
