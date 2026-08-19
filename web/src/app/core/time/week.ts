/**
 * Week arithmetic for the timeline surfaces.
 *
 * Weeks are Monday-to-Friday because the boards show working days; Saturday and Sunday are not hidden columns,
 * they are simply not part of the grid. ISO week numbers are used since that is what French and Spanish business
 * calendars run on, and the design labels the pager "Sem. 34".
 */
export interface WorkWeek {
  readonly isoWeek: number;
  readonly days: readonly Date[];
  readonly monday: Date;
  readonly friday: Date;
}

const WORKING_DAYS = 5;

export function startOfWeek(reference: Date): Date {
  const date = new Date(reference.getFullYear(), reference.getMonth(), reference.getDate());
  // getDay() is 0 for Sunday, so Sunday must step back six days rather than forward one.
  const offsetToMonday = (date.getDay() + 6) % 7;

  date.setDate(date.getDate() - offsetToMonday);

  return date;
}

export function workWeek(offsetInWeeks = 0, reference: Date = new Date()): WorkWeek {
  const monday = startOfWeek(reference);
  monday.setDate(monday.getDate() + offsetInWeeks * 7);

  const days = Array.from({ length: WORKING_DAYS }, (_, index) => {
    const day = new Date(monday);
    day.setDate(monday.getDate() + index);

    return day;
  });

  return {
    isoWeek: isoWeekNumber(monday),
    days,
    monday,
    friday: days[WORKING_DAYS - 1]!,
  };
}

/**
 * ISO 8601: week 1 is the week containing the first Thursday of the year. Computed from the Thursday of the
 * reference week, which is what makes the year boundary come out right without special-casing it.
 */
export function isoWeekNumber(reference: Date): number {
  const thursday = new Date(reference.getFullYear(), reference.getMonth(), reference.getDate());
  thursday.setDate(thursday.getDate() + 3 - ((thursday.getDay() + 6) % 7));

  const firstThursday = new Date(thursday.getFullYear(), 0, 4);
  firstThursday.setDate(firstThursday.getDate() + 3 - ((firstThursday.getDay() + 6) % 7));

  return 1 + Math.round((thursday.getTime() - firstThursday.getTime()) / (7 * 24 * 3600 * 1000));
}

/** "17/08" — the compact form the timeline headers and the week pager use. */
export function formatDayMonth(date: Date): string {
  return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}`;
}

export function isSameDay(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
  );
}
