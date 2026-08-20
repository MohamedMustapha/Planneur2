import { computed, Injectable, inject } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * What `/bff/user` returns. Mirrors `BffUser` on the server; the two are one contract.
 */
export interface SessionUser {
  readonly isAuthenticated: boolean;
  readonly id: string;
  readonly userName: string;
  readonly displayName: string | null;
  readonly unitId: string | null;
  readonly departmentIds: readonly string[];
  readonly roles: readonly string[];
  readonly functionalRole: string | null;
  readonly language: string;
}

export const ANONYMOUS_SESSION: SessionUser = {
  isAuthenticated: false,
  id: '',
  userName: '',
  displayName: null,
  unitId: null,
  departmentIds: [],
  roles: [],
  functionalRole: null,
  language: 'fr',
};

/**
 * The session, as a signal store — a plain injectable exposing signals, which is what `architecture.md §7` means
 * by "no external state management". There is no NgRx here and there does not need to be.
 *
 * Roles held here drive *rendering only*: which nav entries appear, which buttons are shown. They never decide
 * what data comes back. If the UI and RLS ever disagree the user sees an empty table, not someone else's rows.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly http = inject(HttpClient);

  private readonly resource = httpResource<SessionUser>(() => '/bff/user', {
    defaultValue: ANONYMOUS_SESSION,
  });

  readonly user = computed(() => this.resource.value() ?? ANONYMOUS_SESSION);
  readonly isLoading = this.resource.isLoading;
  readonly isAuthenticated = computed(() => this.user().isAuthenticated);
  readonly roles = computed(() => this.user().roles);

  readonly initials = computed(() => {
    const user = this.user();
    const source = user.displayName ?? user.userName;

    return source
      .split(/[\s.]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  });

  readonly displayName = computed(() => this.user().displayName ?? this.user().userName);

  has(role: string): boolean {
    return this.user().roles.includes(role);
  }

  hasAny(...roles: readonly string[]): boolean {
    return roles.some((role) => this.has(role));
  }

  /** Full-page navigation on purpose: the OIDC handshake is a browser redirect, not an XHR. */
  login(returnUrl: string = window.location.pathname): void {
    window.location.href = `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`;
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post('/bff/logout', null, { observe: 'response' }));
    window.location.href = '/';
  }

  reload(): void {
    this.resource.reload();
  }
}
