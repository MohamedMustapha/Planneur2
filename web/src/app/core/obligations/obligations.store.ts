import { computed, inject, Injectable, signal } from '@angular/core';
import { ActivitiesStore } from '../activities/activities.store';

/** Severity decides the chip's colour, and nothing else. Three levels because the theme defines three. */
export type ObligationSeverity = 'danger' | 'warning';

export interface Obligation {
  /** Stable across recomputations — it is what a dismissal is recorded against. */
  readonly id: string;
  /** Transloco key plus params, so the chip re-labels on a language switch like everything else. */
  readonly labelKey: string;
  readonly params: Record<string, unknown>;
  readonly severity: ObligationSeverity;
}

/**
 * The one thing allowed to pierce Focus mode — v2 §02.2.
 *
 * "Focus mode never hides an item the user has an open obligation on." An obligation is not a notification: it is
 * something the viewer owes somebody, which the tool would be lying by omission to conceal behind a mode whose
 * whole purpose is concealment. Everything else Focus mode hides; these it shows, as a dismissible chip.
 *
 * The spec names three sources — an overdue action item (`07`), a problem awaiting triage (`05`), and an over-target
 * week (S5). Only the third exists in this build, so only the third is wired. The other two are deliberately
 * absent rather than faked: a chip that always says "0 problems await triage" trains people to ignore the chip,
 * which costs more than the missing feature does. When those slices land they add a computed to this class and
 * nothing else changes — which is why this is a store rather than three inline conditions in the top bar.
 */
@Injectable({ providedIn: 'root' })
export class ObligationsStore {
  private readonly activities = inject(ActivitiesStore);

  /**
   * Ids the viewer has waved away this session.
   *
   * Session-scoped on purpose: an obligation is a live fact, not a message, so dismissing it hides the chip until
   * the next load rather than settling the thing it is about. Persisting a dismissal would let somebody silence a
   * genuine overrun permanently, which is the opposite of what an obligation is for.
   */
  private readonly dismissed = signal<readonly string[]>([]);

  private readonly all = computed<readonly Obligation[]>(() => {
    const obligations: Obligation[] = [];

    if (this.activities.isOverTarget()) {
      obligations.push({
        id: 'week-over-target',
        labelKey: 'obligations.weekOverTarget',
        params: { hours: this.activities.overtime() },
        severity: 'warning',
      });
    }

    return obligations;
  });

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

  dismiss(id: string): void {
    this.dismissed.update((ids) => (ids.includes(id) ? ids : [...ids, id]));
  }
}
