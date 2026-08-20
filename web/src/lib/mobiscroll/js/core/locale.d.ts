import localeDe from '../i18n/de';
import localeEl from '../i18n/el';
import localeEs from '../i18n/es';
import localeFr from '../i18n/fr';
import localeIt from '../i18n/it';
import { MbscLocale } from '../i18n/locale';
export * from '../i18n/locale';
declare const localeEn: MbscLocale;
declare const locale: {
    [key: string]: MbscLocale;
};
export { locale, localeDe, localeEl, localeEn, localeEs, localeFr, localeIt, };
export * from '../i18n/hijri';
export * from '../i18n/jalali';
