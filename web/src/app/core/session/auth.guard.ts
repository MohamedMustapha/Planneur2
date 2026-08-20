import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { SessionStore } from './session.store';

/**
 * Sends an unauthenticated visitor to Keycloak.
 *
 * This is a convenience, not a security control — it decides which screen renders, and the data on that screen is
 * still gated by the API and by RLS. A guard that could be bypassed by editing the URL would be a problem only if
 * anything downstream trusted it, and nothing does.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionStore);

  // The session resource may still be in flight on a cold load; wait for the first settled value rather than
  // bouncing the user to a login they do not need.
  while (session.isLoading()) {
    await new Promise((resolve) => setTimeout(resolve, 20));
  }

  if (session.isAuthenticated()) {
    return true;
  }

  session.login(state.url);

  return false;
};
