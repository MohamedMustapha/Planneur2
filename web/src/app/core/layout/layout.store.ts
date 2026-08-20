import { computed, effect, Injectable, signal } from '@angular/core';

const RAIL_STORAGE_KEY = 'cracra.rail.expanded';

/**
 * Shell chrome state: rail width, and the department currently in scope.
 *
 * The department switcher is a *view* scope, not a permission change. Picking one narrows what the boards query;
 * it cannot widen what the viewer is allowed to see, because that is decided server-side from the token.
 */
@Injectable({ providedIn: 'root' })
export class LayoutStore {
  readonly railExpanded = signal<boolean>(localStorage.getItem(RAIL_STORAGE_KEY) !== 'false');

  readonly railWidth = computed(() =>
    this.railExpanded() ? 'var(--rail-width-expanded)' : 'var(--rail-width-collapsed)',
  );

  /**
   * Week offset from the current one; 0 is today's week. The whole shell shares it so the top-bar pager and every
   * board stay on the same week — the design's pager sits above the content precisely because it is shell state.
   */
  readonly weekOffset = signal(0);

  constructor() {
    effect(() => localStorage.setItem(RAIL_STORAGE_KEY, String(this.railExpanded())));
  }

  toggleRail(): void {
    this.railExpanded.update((expanded) => !expanded);
  }

  previousWeek(): void {
    this.weekOffset.update((offset) => offset - 1);
  }

  nextWeek(): void {
    this.weekOffset.update((offset) => offset + 1);
  }

  today(): void {
    this.weekOffset.set(0);
  }
}
