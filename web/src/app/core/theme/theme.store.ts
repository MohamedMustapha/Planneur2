import { effect, Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'cracra.theme';

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
  }

  private static initial(): Theme {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (stored === 'light' || stored === 'dark') {
      return stored;
    }

    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
}
