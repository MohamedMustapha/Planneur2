import { Pipe, PipeTransform, inject } from '@angular/core';
import { formatNumber } from '@angular/common';

import { LanguageStore } from './language.store';

/**
 * Angular's `number`, in the language the reader actually chose.
 *
 * The built-in pipe formats against `LOCALE_ID`, which is resolved once per injector and therefore cannot follow a
 * language that switches at runtime — and, left unprovided, is `en-US` for everybody. The visible result was a
 * French screen answering "125,000" for a hundred and twenty-five thousand euros, which in French reads as a
 * hundred and twenty-five. Reading the active language here instead is what makes the figure mean what it says.
 *
 * Pure, and still correct across a switch: every screen renders inside `*transloco`, whose re-render on language
 * change rebuilds the embedded view and with it this pipe.
 */
@Pipe({ name: 'localizedNumber' })
export class LocalizedNumber implements PipeTransform {
  private readonly languages = inject(LanguageStore);

  transform(value: number | string | null | undefined, digitsInfo?: string): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }

    const numeric = typeof value === 'number' ? value : Number(value);

    return Number.isNaN(numeric) ? '' : formatNumber(numeric, this.languages.language(), digitsInfo);
  }
}
