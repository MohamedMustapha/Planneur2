import { effect, inject, Injectable } from '@angular/core';
import { localeEn, localeEs, localeFr, MbscLocale, setOptions } from '@mobiscroll/angular';
import { Language, LanguageStore } from '../i18n/language.store';
import { ThemeStore } from './theme.store';

/**
 * The three dictionaries the platform ships, by our own language code.
 *
 * Imported from the package rather than read off a global. The vendored bundle is an ES module — nothing it
 * exports is hung on `window` — so a lookup like `globalThis.mobiscroll.localeFr` resolves to `undefined` and the
 * component silently falls back to English, which is exactly the failure this map replaces.
 */
const LOCALES: Record<Language, MbscLocale> = {
  fr: localeFr,
  en: localeEn,
  es: localeEs,
};

/**
 * Keeps Mobiscroll's global options in step with the shell's theme and language.
 *
 * Global rather than per-component, which is what Mobiscroll's own "global options" are for. Every calendar,
 * popup, datepicker and input the product renders has to answer light-or-dark and which-language identically, and
 * the alternative — repeating `theme`, `themeVariant` and `locale` in each component's options object — is a rule
 * that holds until somebody adds the next component and forgets one of the three.
 *
 * Two things were wrong before this existed, and they were wrong in the same place:
 *
 * `themeVariant: 'auto'` reads `prefers-color-scheme`, which is the *operating system's* preference. The shell's
 * toggle writes `data-theme` on the document and stores a choice that deliberately outranks the OS — so a user on
 * a light desktop who switched the app to dark got a dark page with a light timeline in the middle of it. Auto is
 * the right default for a library that has no idea what the host application thinks; it is the wrong answer for
 * one that does.
 *
 * The locale was read from a global that an ES module never populates, so every calendar rendered its day names
 * in English regardless of the language switch — on a product whose default language is French.
 *
 * Resolved once at startup and left running: the effect re-applies on every change, and Mobiscroll re-renders
 * mounted components from its own global-change stream, so switching either one takes effect without a reload.
 */
@Injectable({ providedIn: 'root' })
export class MobiscrollOptions {
  private readonly theme = inject(ThemeStore);
  private readonly language = inject(LanguageStore);

  constructor() {
    effect(() => {
      setOptions({
        // One theme, both variants. The design tokens are Material-shaped, and offering iOS or Windows here would
        // mean a calendar that no longer matches the buttons around it.
        theme: 'material',
        themeVariant: this.theme.theme(),
        locale: LOCALES[this.language.language()],
      });
    });
  }
}
