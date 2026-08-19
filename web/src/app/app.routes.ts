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
  placeholder('team', { titleKey: 'nav.myTeam', slice: 'S6', descriptionKey: 'placeholder.team' }),
  {
    // S1 delivers the org explorer; the timeline view of a unit still belongs to S6.
    path: 'unit',
    canActivate: [authGuard],
    loadComponent: () => import('./features/directory/org-explorer').then((m) => m.OrgExplorer),
  },
  placeholder('department', { titleKey: 'nav.department', slice: 'S6', descriptionKey: 'placeholder.department' }),
  placeholder('projects', { titleKey: 'nav.projects', slice: 'S3', descriptionKey: 'placeholder.projects' }),
  placeholder('portfolio', { titleKey: 'nav.portfolio', slice: 'S4', descriptionKey: 'placeholder.portfolio' }),
  placeholder('reports', { titleKey: 'nav.reports', slice: 'S8', descriptionKey: 'placeholder.reports' }),
  placeholder('kudos', { titleKey: 'nav.kudos', slice: 'S9', descriptionKey: 'placeholder.kudos' }),
  placeholder('finance', { titleKey: 'nav.finance', slice: 'S11', descriptionKey: 'placeholder.finance' }),
  {
    path: 'settings',
    canActivate: [authGuard],
    loadComponent: () => import('./features/directory/department-settings').then((m) => m.DepartmentSettings),
  },
  { path: '**', redirectTo: 'board' },
];
