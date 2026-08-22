import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { FocusStore } from './core/focus/focus.store';
import { LanguageStore } from './core/i18n/language.store';
import { SessionStore } from './core/session/session.store';
import { MobiscrollOptions } from './core/theme/mobiscroll-options';
import { ThemeStore } from './core/theme/theme.store';
import { NavRail } from './shell/nav-rail/nav-rail';
import { TopBar } from './shell/top-bar/top-bar';

/**
 * The application shell: a persistent left rail, a top bar, and a content area the router fills.
 *
 * Everything below the shell is a slice. The shell itself owns only what is true on every screen — who you are,
 * which department and week you are looking at, and which language, theme and shell mode you are looking in.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, NavRail, TopBar, TranslocoDirective],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly session = inject(SessionStore);
  private readonly language = inject(LanguageStore);

  protected readonly focus = inject(FocusStore);

  constructor() {
    // Instantiated for its constructor effect, which puts data-theme on <html> before first paint. Nothing here
    // reads it back, so it is resolved rather than stored.
    inject(ThemeStore);

    // Same reason: it exists to push the shell's theme and language into Mobiscroll's global options. Resolved in
    // the shell rather than lazily wherever the first calendar happens to be, so a component mounted mid-session
    // is never the thing that decides what language the whole library speaks.
    inject(MobiscrollOptions);

    // The profile language is a default, not an override: adoptProfileLanguage backs off if this browser already
    // holds a deliberate choice.
    effect(() => {
      const user = this.session.user();

      if (user.isAuthenticated) {
        this.language.adoptProfileLanguage(user.language);
      }
    });
  }
}
