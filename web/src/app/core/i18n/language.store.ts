import { effect, Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

export const SUPPORTED_LANGUAGES = ['fr', 'en', 'es'] as const;

export type Language = (typeof SUPPORTED_LANGUAGES)[number];

export const DEFAULT_LANGUAGE: Language = 'fr';

const STORAGE_KEY = 'cracra.language';

/**
 * The active UI language.
 *
 * French is the default and, per the design brief, the width reference — every layout is checked against French
 * strings because they are the longest of the three, so a layout that fits French fits the other two.
 *
 * Switching is runtime, with no reload: Transloco swaps the dictionary and the signals re-render.
 */
@Injectable({ providedIn: 'root' })
export class LanguageStore {
  private readonly transloco = inject(TranslocoService);

  readonly language = signal<Language>(LanguageStore.initial());

  readonly available = SUPPORTED_LANGUAGES;

  constructor() {
    effect(() => {
      const language = this.language();

      this.transloco.setActiveLang(language);
      document.documentElement.lang = language;
      localStorage.setItem(STORAGE_KEY, language);
    });
  }

  set(language: Language): void {
    this.language.set(language);
  }

  /**
   * Adopts the language from the user's Keycloak profile, but only while they have not chosen one in this browser.
   * A deliberate in-app switch should survive a page load.
   */
  adoptProfileLanguage(language: string): void {
    if (localStorage.getItem(STORAGE_KEY)) {
      return;
    }

    if (LanguageStore.isSupported(language)) {
      this.language.set(language);
    }
  }

  private static initial(): Language {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (stored && LanguageStore.isSupported(stored)) {
      return stored;
    }

    const browser = navigator.language?.split('-')[0] ?? '';

    return LanguageStore.isSupported(browser) ? browser : DEFAULT_LANGUAGE;
  }

  private static isSupported(candidate: string): candidate is Language {
    return (SUPPORTED_LANGUAGES as readonly string[]).includes(candidate);
  }
}
