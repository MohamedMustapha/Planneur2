import { registerLocaleData } from '@angular/common';
import localeEs from '@angular/common/locales/es';
import localeFr from '@angular/common/locales/fr';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

// Angular ships only en-US compiled in. Without these two, `formatNumber` falls back to English grouping for a
// French or Spanish reader — which is not a cosmetic difference: "125,000" and "125 000" are different numbers to
// somebody reading in French.
registerLocaleData(localeFr);
registerLocaleData(localeEs);

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
