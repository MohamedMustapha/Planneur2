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
    path: 'portfolio',
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
    path: 'finance',
    canActivate: [authGuard],
    loadComponent: () => import('./features/finance/capex-opex').then((m) => m.CapexOpex),
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
    // Behind the same nav entry as department settings: the rail is fixed at ten sections by the design, and
    // both screens are the same job from an administrator's point of view.
    path: 'settings/access',
    canActivate: [authGuard],
    loadComponent: () => import('./features/access/rbac-admin').then((m) => m.RbacAdmin),
  },
  { path: '**', redirectTo: 'board' },
];
