import { Routes } from '@angular/router';
import { authGuard } from './core/session/auth.guard';
import { SlicePlaceholderData } from './features/placeholder/slice-placeholder';

/**
 * One route per rail entry, so the shell is fully navigable from S0 and each later slice replaces a placeholder
 * rather than adding a route. Everything is lazy: the shell is the only thing in the initial bundle.
 */
function placeholder(path: string, data: SlicePlaceholderData) {
  return {
    path,
    canActivate: [authGuard],
    data,
    loadComponent: () =>
      import('./features/placeholder/slice-placeholder').then((m) => m.SlicePlaceholder),
  };
}

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
    loadComponent: () => import('./features/portfolio/portfolio-board').then((m) => m.PortfolioBoard),
  },
  placeholder('reports', { titleKey: 'nav.reports', slice: 'S8', descriptionKey: 'placeholder.reports' }),
  placeholder('kudos', { titleKey: 'nav.kudos', slice: 'S9', descriptionKey: 'placeholder.kudos' }),
  placeholder('finance', { titleKey: 'nav.finance', slice: 'S11', descriptionKey: 'placeholder.finance' }),
  {
    path: 'settings',
    canActivate: [authGuard],
    loadComponent: () => import('./features/directory/department-settings').then((m) => m.DepartmentSettings),
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
