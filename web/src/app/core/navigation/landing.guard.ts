import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { NavigationStore } from './navigation.store';

/** The empty path: whatever the server says this viewer's intent is (v2 §02.1). */
export const landingGuard: CanActivateFn = async () => {
  const navigation = inject(NavigationStore);
  const router = inject(Router);

  await navigation.settled();

  return router.parseUrl(navigation.landingRoute());
};
