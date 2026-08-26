import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DirectoryStore } from '../directory/directory.store';
import { Me } from '../directory/directory.models';
import { FocusStore } from '../focus/focus.store';
import { LanguageStore } from '../i18n/language.store';
import { ThemeStore } from '../theme/theme.store';
import { PreferencesStore } from './preferences.store';

/**
 * Where a person's display preferences are kept, and which answer wins.
 *
 * The precedence runs one way — a choice made here beats this browser's cache, which beats what the directory
 * synced from Keycloak, which beats what the platform guesses — and every bug this store can have is a step of
 * that ladder being skipped: a saved setting reverted by the next `/me`, a zone silently replaced under somebody
 * who picked one, or a Focus mode turned *off* being dropped on the way to the server because `false` is falsy.
 */
describe('PreferencesStore', () => {
  interface Harness {
    readonly store: PreferencesStore;
    readonly http: HttpTestingController;
    readonly directory: { me: ReturnType<typeof signal<Me | null>>; reloaded: number };
    readonly language: { adopted: string[]; chosen: string[] };
    readonly theme: { adopted: (string | null)[]; chosen: string[] };
    readonly focus: { adopted: (boolean | null)[]; chosen: boolean | null };
  }

  function harness(): Harness {
    TestBed.resetTestingModule();

    const me = signal<Me | null>(null);
    const directory = { me, reloaded: 0, reload: () => (directory.reloaded += 1) };

    const language = {
      adopted: [] as string[],
      chosen: [] as string[],
      language: signal<'fr' | 'en' | 'es'>('fr'),
      adoptProfileLanguage(value: string) {
        this.adopted.push(value);
      },
      // Records the call and applies it, because the PUT body is built from language() and the point of several
      // of these scenarios is which value ends up in it.
      set(value: 'fr' | 'en' | 'es') {
        this.chosen.push(value);
        this.language.set(value);
      },
    };

    const theme = {
      adopted: [] as (string | null)[],
      chosen: [] as string[],
      theme: signal<'light' | 'dark'>('light'),
      adoptProfileTheme(value: string | null) {
        this.adopted.push(value);
      },
      choose(value: 'light' | 'dark') {
        this.chosen.push(value);
        this.theme.set(value);
      },
    };

    const focus = {
      adopted: [] as (boolean | null)[],
      chosen: null as boolean | null,
      adoptProfileFocusMode(value: boolean | null) {
        this.adopted.push(value);
      },
      choose(value: boolean) {
        this.chosen = value;
      },
    };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DirectoryStore, useValue: directory },
        { provide: LanguageStore, useValue: language },
        { provide: ThemeStore, useValue: theme },
        { provide: FocusStore, useValue: focus },
      ],
    });

    return {
      store: TestBed.inject(PreferencesStore),
      http: TestBed.inject(HttpTestingController),
      directory,
      language,
      theme,
      focus,
    };
  }

  /** One record for the tests that only care about a field or two of it. */
  function meWith(overrides: Partial<Me>): Me {
    return {
      personId: 'c0000000-0000-0000-0000-000000000001',
      displayName: 'Camille Villeneuve',
      email: null,
      ldapUid: 'camille.villeneuve',
      primaryUnitId: null,
      primaryDepartmentId: null,
      timeZone: 'Europe/Paris',
      uiLanguage: 'fr',
      preferredLanguage: null,
      preferredTimeZone: null,
      preferredTheme: null,
      focusMode: null,
      units: [],
      departments: [],
      functionalRoleCodes: [],
      contextualRoles: [],
      profile: null,
      ...overrides,
    };
  }

  beforeEach(() => localStorage.clear());

  it('applies a choice before the round trip and sends the whole record', async () => {
    const { store, http, theme } = harness();

    const saving = store.set({ theme: 'dark' });

    // Local first: the person pressed a button and the screen answers now, not after the network.
    expect(theme.chosen).toEqual(['dark']);

    const request = http.expectOne('/api/directory/me/preferences');

    expect(request.request.method).toBe('PUT');
    expect(request.request.body.theme).toBe('dark');
    expect(request.request.body.language).toBe('fr');

    request.flush({});
    await saving;

    http.verify();
  });

  it('sends Focus mode turned off rather than dropping it', async () => {
    const { store, http, focus } = harness();

    const saving = store.set({ focusMode: false });

    // The `!== undefined` rule. A truthiness check here would silently discard exactly half of a toggle, and the
    // setting would appear to revert on the next load.
    expect(focus.chosen).toBe(false);

    const request = http.expectOne('/api/directory/me/preferences');

    expect(request.request.body.focusMode).toBe(false);

    request.flush({});
    await saving;

    http.verify();
  });

  it('carries a never-chosen Focus mode through as null', async () => {
    const { store, http } = harness();

    const saving = store.set({ language: 'en' });

    const request = http.expectOne('/api/directory/me/preferences');

    // Null has to survive the round trip: it is what keeps the role default in charge for somebody who has never
    // reached for the toggle.
    expect(request.request.body.focusMode).toBeNull();

    request.flush({});
    await saving;
  });

  it('re-reads the directory once the save lands', async () => {
    const { store, http, directory } = harness();

    const saving = store.set({ language: 'es' });

    http.expectOne('/api/directory/me/preferences').flush({});
    await saving;

    // The record the rest of the app reads from is now stale by one save; nothing else would notice.
    expect(directory.reloaded).toBe(1);
    expect(store.error()).toBeNull();
    expect(store.saving()).toBe(false);
  });

  it('keeps the choice and says so when the save fails', async () => {
    const { store, http, theme } = harness();

    const saving = store.set({ theme: 'dark' });

    http
      .expectOne('/api/directory/me/preferences')
      .flush(
        { detail: "'de' is not a supported language." },
        { status: 422, statusText: 'Unprocessable' },
      );

    await saving;

    // Losing the setting somebody just made because the network blinked is the worse of the two failures, so the
    // local choice stands and the message explains why it did not stick.
    expect(theme.chosen).toEqual(['dark']);
    expect(store.error()).toBe("'de' is not a supported language.");
    expect(store.saving()).toBe(false);
  });

  it('falls back to the problem title, then to a key', async () => {
    const withTitle = harness();

    const first = withTitle.store.set({ language: 'en' });

    withTitle.http
      .expectOne('/api/directory/me/preferences')
      .flush({ title: 'Access denied.' }, { status: 403, statusText: 'Forbidden' });

    await first;
    expect(withTitle.store.error()).toBe('Access denied.');

    const bare = harness();

    const second = bare.store.set({ language: 'en' });

    bare.http
      .expectOne('/api/directory/me/preferences')
      .flush(null, { status: 500, statusText: 'Server Error' });

    await second;

    // A translation key rather than a raw status, so a failure nobody wrote a message for still reads as French
    // in the panel it appears in.
    expect(bare.store.error()).toBe('preferences.saveFailed');
  });

  it('clears a previous failure when the next save starts', async () => {
    const { store, http } = harness();

    const failing = store.set({ language: 'en' });

    http
      .expectOne('/api/directory/me/preferences')
      .flush(null, { status: 500, statusText: 'Server Error' });
    await failing;

    const succeeding = store.set({ language: 'es' });

    expect(store.error()).toBeNull();

    http.expectOne('/api/directory/me/preferences').flush({});
    await succeeding;
  });

  it('adopts what the server holds for this person', () => {
    const { directory, language, theme, focus } = harness();

    directory.me.set(
      meWith({
        preferredLanguage: 'en',
        preferredTheme: 'dark',
        focusMode: true,
        preferredTimeZone: 'Asia/Tokyo',
      }),
    );

    TestBed.tick();

    expect(language.adopted).toEqual(['en']);
    expect(theme.adopted).toEqual(['dark']);
    expect(focus.adopted).toEqual([true]);
  });

  it('treats the synced values as a seed where the person has chosen nothing', () => {
    const { directory, language, theme, focus } = harness();

    directory.me.set(meWith({ uiLanguage: 'es' }));

    TestBed.tick();

    // The Keycloak locale still decides the first render; what it must not do is compete with a choice the person
    // made in the app, which is why the preferred value is consulted first and only falls through when null.
    expect(language.adopted).toEqual(['es']);
    expect(theme.adopted).toEqual([null]);
    expect(focus.adopted).toEqual([null]);
  });

  it('takes the stored zone when this browser has never picked one', () => {
    const { store, directory } = harness();

    directory.me.set(meWith({ preferredTimeZone: 'Asia/Tokyo' }));
    TestBed.tick();

    expect(store.timeZone()).toBe('Asia/Tokyo');
  });

  it('falls back to the synced zone before the browser’s', () => {
    const { store, directory } = harness();

    directory.me.set(meWith({ timeZone: 'America/Montreal' }));
    TestBed.tick();

    expect(store.timeZone()).toBe('America/Montreal');
  });

  it('does not overwrite a zone picked on this machine', () => {
    localStorage.setItem('cracra.timezone', 'Europe/Madrid');
    localStorage.setItem('cracra.timezone.chosen', 'true');

    const { store, directory } = harness();

    directory.me.set(meWith({ preferredTimeZone: 'Asia/Tokyo' }));
    TestBed.tick();

    // The zone decides what hour an entry is recorded at. Silently moving somebody's day because the server
    // holds a different answer is the one failure here that corrupts data rather than merely annoying.
    expect(store.timeZone()).toBe('Europe/Madrid');
  });

  it('records that a zone was chosen here, not merely cached here', async () => {
    const { store, http } = harness();

    const saving = store.set({ timeZone: 'Asia/Tokyo' });

    http.expectOne('/api/directory/me/preferences').flush({});
    await saving;
    TestBed.tick();

    expect(store.timeZone()).toBe('Asia/Tokyo');
    expect(localStorage.getItem('cracra.timezone.chosen')).toBe('true');
    expect(localStorage.getItem('cracra.timezone')).toBe('Asia/Tokyo');
  });

  it('offers the zones the platform knows', () => {
    const { store } = harness();

    // From Intl rather than a curated list, so the picker does not start omitting places people actually work.
    expect(store.availableZones()).toContain('Europe/Paris');
    expect(store.languages).toEqual(['fr', 'en', 'es']);
  });
});
