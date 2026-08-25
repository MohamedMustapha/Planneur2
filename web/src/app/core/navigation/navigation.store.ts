import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { CapabilityStore } from '../capabilities/capability.store';
import { FALLBACK_NAVIGATION, NavigationSection, routeFor, sectionsFor } from './navigation';

export interface ShellNavigation {
  readonly position: string;
  readonly landingId: string;
  readonly focusId: string;
  readonly primary: readonly string[];
  readonly secondary: readonly string[];
}

/**
 * The shell the server computed for this viewer (v2 §02.1). Hiding an entry is presentation; RLS decides what a
 * deep link returns.
 */
@Injectable({ providedIn: 'root' })
export class NavigationStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);
  private readonly capabilities = inject(CapabilityStore);

  private readonly fetched = signal<ShellNavigation | null>(null);

  private pending: Promise<void> | null = null;

  readonly shell = computed<ShellNavigation>(() => this.fetched() ?? FALLBACK_NAVIGATION);

  readonly position = computed(() => this.shell().position);

  readonly primary = computed<readonly NavigationSection[]>(() => this.allowed(this.shell().primary));

  readonly secondary = computed<readonly NavigationSection[]>(() =>
    this.allowed(this.shell().secondary),
  );

  readonly landingRoute = computed(() => routeFor(this.shell().landingId));

  readonly focusRoute = computed(() => routeFor(this.shell().focusId));

  constructor() {
    effect(() => {
      if (this.session.isAuthenticated()) {
        void this.reload();
      }
    });
  }

  async reload(): Promise<void> {
    this.pending = this.load();

    await this.pending;
  }

  /** Resolves once the server has answered. The landing redirect waits on this rather than bouncing. */
  async settled(): Promise<void> {
    // The guard runs before the constructor's effect does, so this is usually what starts the fetch.
    this.pending ??= this.load();

    await this.pending;
  }

  private async load(): Promise<void> {
    // A failure leaves the member fallback standing: a narrow rail, not a broken one.
    try {
      this.fetched.set(
        await firstValueFrom(this.http.get<ShellNavigation>('/api/guidance/navigation')),
      );
    } catch {
      // Left alone deliberately.
    }
  }

  private allowed(ids: readonly string[]): readonly NavigationSection[] {
    return sectionsFor(ids).filter(
      (section) => !section.requiresCapability || this.capabilities.allows(section.requiresCapability),
    );
  }
}
