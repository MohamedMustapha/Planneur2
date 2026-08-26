import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';

/** Severity decides the chip's colour, and nothing else. */
export type ObligationSeverity = 'danger' | 'warning';

export interface Obligation {
  /** Stable across recomputations — it is what a dismissal is recorded against. */
  readonly id: string;
  readonly labelKey: string;
  readonly params: Readonly<Record<string, string>>;
  readonly severity: ObligationSeverity;
}

interface ObligationDto {
  readonly id: string;
  readonly key: string;
  readonly params: Readonly<Record<string, string>>;
  readonly severity: string;
}

/**
 * The one thing allowed to pierce Focus mode — v2 §02.2. All three sources (overdue action, problem awaiting
 * triage, over-target week) are computed server-side and arrive together.
 */
@Injectable({ providedIn: 'root' })
export class ObligationsStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  private readonly fetched = signal<readonly ObligationDto[]>([]);

  /** Session-scoped on purpose: dismissing hides the chip, it does not settle the thing behind it. */
  private readonly dismissed = signal<readonly string[]>([]);

  private readonly all = computed<readonly Obligation[]>(() =>
    this.fetched().map((row) => ({
      id: row.id,
      labelKey: row.key,
      params: row.params,
      severity: row.severity === 'danger' ? 'danger' : 'warning',
    })),
  );

  readonly obligations = computed(() =>
    this.all().filter((obligation) => !this.dismissed().includes(obligation.id)),
  );

  readonly hasAny = computed(() => this.obligations().length > 0);

  /** The one the Focus banner has room for: the most severe, and the first among equals. */
  readonly top = computed<Obligation | null>(
    () =>
      this.obligations().find((obligation) => obligation.severity === 'danger') ??
      this.obligations()[0] ??
      null,
  );

  constructor() {
    effect(() => {
      if (this.session.isAuthenticated()) {
        void this.reload();
      }
    });
  }

  dismiss(id: string): void {
    this.dismissed.update((ids) => (ids.includes(id) ? ids : [...ids, id]));
  }

  async reload(): Promise<void> {
    // A failure leaves the last answer standing: a chip that vanishes on a timeout is the concealment this
    // store exists to prevent.
    try {
      this.fetched.set(
        await firstValueFrom(this.http.get<readonly ObligationDto[]>('/api/guidance/obligations')),
      );
    } catch {
      // Left alone deliberately.
    }
  }
}
