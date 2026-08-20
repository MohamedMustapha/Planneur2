import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Translation, TranslocoLoader } from '@jsverse/transloco';

/**
 * Loads `assets/i18n/{lang}.json`. Dictionaries are fetched rather than bundled so a wording fix is a file swap on
 * the server, not a rebuild and redeploy of the whole client — which matters when three languages are maintained
 * by people who are not the ones deploying.
 */
@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string) {
    return this.http.get<Translation>(`/assets/i18n/${lang}.json`);
  }
}
