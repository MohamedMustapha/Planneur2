import { effect, Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'cracra.theme';

/**
 * Whether the theme in storage was *chosen* here, as opposed to merely cached here.
 *
 * A second key rather than a flag inside the first, because the first is written on every render by the effect
 * below — it is a cache of the current value, not a record of intent. Reading it as intent is the trap: it is
 * always set by the time anything asks, so a guard built on it never opens.
 */
const CHOICE_KEY = 'cracra.theme.chosen';

/**
 * Light/dark, toggled from the top bar as the design specifies.
 *
 * The stored preference wins over the OS setting, because someone who reached for the toggle has expressed an
 * opinion; the OS is consulted only when they have not. The theme is applied as `data-theme` on the document
 * element, which is the single hook `_tokens.scss` keys off.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  readonly theme = signal<Theme>(ThemeStore.initial());

  constructor() {
    effect(() => {
      const theme = this.theme();

      document.documentElement.setAttribute('data-theme', theme);
      localStorage.setItem(STORAGE_KEY, theme);
    });
  }

  toggle(): void {
    this.theme.update((current) => (current === 'dark' ? 'light' : 'dark'));
    localStorage.setItem(CHOICE_KEY, 'true');
  }

  /** Reaching for the control is the deliberate act; the signal alone is not. */
  choose(theme: Theme): void {
    this.theme.set(theme);
    localStorage.setItem(CHOICE_KEY, 'true');
  }

  /**
   * Adopts the theme stored against the person, but only while this browser holds no deliberate choice.
   *
   * The mirror of LanguageStore.adoptProfileLanguage, and for the same reason: somebody who reached for the
   * toggle on this machine has expressed an opinion about this machine, and a preference arriving from the server
   * a moment later must not silently undo it.
   */
  adoptProfileTheme(theme: string | null): void {
    if (localStorage.getItem(CHOICE_KEY) || (theme !== 'light' && theme !== 'dark')) {
      return;
    }

    this.theme.set(theme);
  }

  private static initial(): Theme {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (stored === 'light' || stored === 'dark') {
      return stored;
    }

    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
}
