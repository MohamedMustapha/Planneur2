import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { NavigationStore } from '../navigation/navigation.store';

const STORAGE_KEY = 'cracra.coach';

export interface CoachStep {
  readonly titleKey: string;
  readonly bodyKey: string;
}

/** Three steps per position (v2 §02.5). Three, because a tour of eleven screens is one nobody reads. */
const STEPS: Readonly<Record<string, readonly CoachStep[]>> = {
  member: step('member', ['week', 'problems', 'focus']),
  'head-leaf': step('head', ['node', 'brief', 'focus']),
  'head-branch': step('head', ['node', 'brief', 'focus']),
  'head-top': step('head', ['node', 'brief', 'focus']),
  po: step('po', ['items', 'progress', 'focus']),
  pmo: step('pmo', ['catalog', 'objectives', 'focus']),
  admin: step('admin', ['org', 'access', 'focus']),
};

function step(family: string, names: readonly string[]): readonly CoachStep[] {
  return names.map((name) => ({
    titleKey: `coach.${family}.${name}.title`,
    bodyKey: `coach.${family}.${name}.body`,
  }));
}

/** The first-run tour: dismissible forever, replayable from the user menu. Kept per browser, not per person. */
@Injectable({ providedIn: 'root' })
export class CoachStore {
  private readonly navigation = inject(NavigationStore);

  private readonly dismissed = signal(readDismissed());

  readonly index = signal(0);

  readonly steps = computed<readonly CoachStep[]>(
    () => STEPS[this.navigation.position()] ?? STEPS['member'],
  );

  readonly current = computed<CoachStep | null>(() => this.steps()[this.index()] ?? null);

  readonly showing = computed(() => !this.dismissed() && this.current() !== null);

  readonly isLast = computed(() => this.index() >= this.steps().length - 1);

  constructor() {
    effect(() => localStorage.setItem(STORAGE_KEY, this.dismissed() ? 'done' : 'pending'));
  }

  next(): void {
    if (this.isLast()) {
      this.dismiss();

      return;
    }

    this.index.update((index) => index + 1);
  }

  dismiss(): void {
    this.dismissed.set(true);
  }

  replay(): void {
    this.index.set(0);
    this.dismissed.set(false);
  }
}

function readDismissed(): boolean {
  return localStorage.getItem(STORAGE_KEY) === 'done';
}
