import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LanguageStore } from './core/i18n/language.store';
import { SessionStore } from './core/session/session.store';
import { ThemeStore } from './core/theme/theme.store';
import { NavRail } from './shell/nav-rail/nav-rail';
import { TopBar } from './shell/top-bar/top-bar';

/**
 * The application shell: a persistent left rail, a top bar, and a content area the router fills.
 *
 * Everything below the shell is a slice. The shell itself owns only what is true on every screen — who you are,
 * which department and week you are looking at, and which language and theme you are looking in.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, NavRail, TopBar],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly session = inject(SessionStore);
  private readonly language = inject(LanguageStore);

  constructor() {
    // Instantiated for its constructor effect, which puts data-theme on <html> before first paint. Nothing here
    // reads it back, so it is resolved rather than stored.
    inject(ThemeStore);

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
