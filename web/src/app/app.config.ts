import {
  ApplicationConfig,
  isDevMode,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';

import { routes } from './app.routes';
import { csrfInterceptor } from './core/session/csrf.interceptor';
import { DEFAULT_LANGUAGE, SUPPORTED_LANGUAGES } from './core/i18n/language.store';
import { TranslocoHttpLoader } from './core/i18n/transloco-http.loader';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),

    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ scrollPositionRestoration: 'top' })),

    provideHttpClient(withFetch(), withInterceptors([csrfInterceptor])),

    provideTransloco({
      config: {
        availableLangs: [...SUPPORTED_LANGUAGES],
        defaultLang: DEFAULT_LANGUAGE,
        fallbackLang: DEFAULT_LANGUAGE,
        reRenderOnLangChange: true,
        // A missing key should be loud in development and invisible in production: nobody using the tool should
        // ever be shown a raw translation key, and nobody building it should be able to miss one.
        missingHandler: { logMissingKey: isDevMode(), useFallbackTranslation: true },
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
  ],
};
