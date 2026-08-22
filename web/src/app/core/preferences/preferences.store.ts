import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DirectoryStore } from '../directory/directory.store';
import { FocusStore } from '../focus/focus.store';
import { Language, LanguageStore, SUPPORTED_LANGUAGES } from '../i18n/language.store';
import { Theme, ThemeStore } from '../theme/theme.store';
import { browserZone } from '../time/zoned';

/** What `PUT /api/directory/me/preferences` takes. Null on a field hands that choice back to the directory. */
export interface PreferencesUpdate {
  readonly language: string | null;
  readonly timeZone: string | null;
  readonly theme: string | null;
  /** Null is "never chosen" and must survive the round trip — see FocusStore. */
  readonly focusMode: boolean | null;
}

/**
 * The person's own display preferences, and where they are kept.
 *
 * Three settings that used to live only in `localStorage`, which meant they were properties of a browser rather
 * than of a person: signing in from a second machine started over, and the time zone — which decides what hour an
 * entry is recorded at — could silently differ between the two. They are now stored against the directory record
 * and cached locally, so a cold load still renders in the right theme before the API answers.
 *
 * The precedence is deliberate and runs one way: a choice made in the app wins over the browser's cache, which
 * wins over what the directory synced from Keycloak, which wins over what the platform guesses. Each step is a
 * stronger statement of intent than the one below it.
 */
@Injectable({ providedIn: 'root' })
export class PreferencesStore {
  private readonly http = inject(HttpClient);
  private readonly directory = inject(DirectoryStore);
  private readonly language = inject(LanguageStore);
  private readonly theme = inject(ThemeStore);
  private readonly focus = inject(FocusStore);

  /** The zone every board, form and day grouping is rendered in. */
  readonly timeZone = signal<string>(readStoredZone());

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  /**
   * The zones offered in the picker.
   *
   * From the platform rather than a curated list: the zone database changes, and a hand-kept list would start
   * omitting places people actually work. Falls back to the browser's own zone on an engine too old to enumerate.
   */
  readonly availableZones = computed<readonly string[]>(() => {
    const supported = (Intl as unknown as { supportedValuesOf?: (key: string) => string[] })
      .supportedValuesOf;

    return supported ? supported('timeZone') : [browserZone()];
  });

  constructor() {
    // Adopt what the server holds, once it arrives and only where this browser has no deliberate answer of its
    // own. The same rule LanguageStore.adoptProfileLanguage already applied to the Keycloak locale, generalised:
    // a preference the person set here should survive the round trip rather than be overwritten by it.
    effect(() => {
      const me = this.directory.me();

      if (!me) {
        return;
      }

      this.language.adoptProfileLanguage(me.preferredLanguage ?? me.uiLanguage);
      this.theme.adoptProfileTheme(me.preferredTheme ?? null);
      this.focus.adoptProfileFocusMode(me.focusMode ?? null);

      if (!readStoredZone.hasChoice()) {
        this.timeZone.set(me.preferredTimeZone ?? me.timeZone ?? browserZone());
      }
    });

    // The cache, not the record of intent — ZONE_CHOICE_KEY is that, for the reason spelled out in ThemeStore.
    effect(() => localStorage.setItem(ZONE_KEY, this.timeZone()));
  }

  /**
   * Applies a preference locally and records it.
   *
   * Local first, on purpose: the person pressed a button and the screen should answer immediately rather than
   * after a round trip. A failed save leaves the choice applied and says so — losing the setting they just made
   * because the network blinked would be the worse of the two failures.
   */
  async set(
    update: Partial<{ language: Language; timeZone: string; theme: Theme; focusMode: boolean }>,
  ): Promise<void> {
    if (update.language) {
      this.language.set(update.language);
    }

    if (update.theme) {
      this.theme.choose(update.theme);
    }

    // `!== undefined` rather than a truthiness check: turning Focus mode *off* is as much a choice as turning it
    // on, and `if (update.focusMode)` would silently drop exactly that half of the toggle.
    if (update.focusMode !== undefined) {
      this.focus.choose(update.focusMode);
    }

    if (update.timeZone) {
      this.timeZone.set(update.timeZone);
      localStorage.setItem(ZONE_CHOICE_KEY, 'true');
    }

    this.saving.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(
        this.http.put('/api/directory/me/preferences', {
          language: this.language.language(),
          timeZone: this.timeZone(),
          theme: this.theme.theme(),
          focusMode: this.focus.chosen,
        } satisfies PreferencesUpdate),
      );

      this.directory.reload();
    } catch (failure: unknown) {
      const problem = failure as { error?: { detail?: string; title?: string } };

      this.error.set(problem.error?.detail ?? problem.error?.title ?? 'preferences.saveFailed');
    } finally {
      this.saving.set(false);
    }
  }

  readonly languages = SUPPORTED_LANGUAGES;
}

const ZONE_KEY = 'cracra.timezone';
const ZONE_CHOICE_KEY = 'cracra.timezone.chosen';

/**
 * The stored zone, or the browser's.
 *
 * Carries `hasChoice` on the function itself so the constructor can ask the question it actually has — "has this
 * browser been told a zone?" — without a second exported symbol for one call site.
 */
function readStoredZone(): string {
  return localStorage.getItem(ZONE_KEY) ?? browserZone();
}

readStoredZone.hasChoice = (): boolean => localStorage.getItem(ZONE_CHOICE_KEY) !== null;
