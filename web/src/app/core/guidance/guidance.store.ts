import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { routeFor } from '../navigation/navigation';

export interface NextAction {
  readonly key: string;
  readonly params: Readonly<Record<string, string>>;
  readonly section: string;
  readonly actionKey: string;
}

/** The one suggested action, from `GET /api/guidance/next-action` (v2 §02.5). Served, so the rule is testable. */
@Injectable({ providedIn: 'root' })
export class GuidanceStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  private readonly fetched = signal<NextAction | null>(null);

  readonly action = computed<NextAction | null>(() => this.fetched());

  readonly route = computed(() => routeFor(this.action()?.section ?? 'board'));

  constructor() {
    effect(() => {
      if (this.session.isAuthenticated()) {
        void this.reload();
      }
    });
  }

  async reload(): Promise<void> {
    // A failure draws no banner rather than a wrong one.
    try {
      this.fetched.set(
        await firstValueFrom(this.http.get<NextAction>('/api/guidance/next-action')),
      );
    } catch {
      // Left alone deliberately.
    }
  }
}
