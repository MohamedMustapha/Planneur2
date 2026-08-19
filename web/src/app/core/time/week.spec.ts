import { describe, expect, it } from 'vitest';
import { formatDayMonth, isoWeekNumber, startOfWeek, workWeek } from './week';

describe('workWeek', () => {
  it('starts on Monday whatever day you ask from', () => {
    // Wednesday 19 August 2026.
    const week = workWeek(0, new Date(2026, 7, 19));

    expect(week.monday.getDay()).toBe(1);
    expect(formatDayMonth(week.monday)).toBe('17/08');
    expect(formatDayMonth(week.friday)).toBe('21/08');
  });

  it('treats Sunday as the end of the week it belongs to, not the start of the next', () => {
    // getDay() is 0 for Sunday, so the naive arithmetic jumps forward a week here. Sunday 23 August 2026 belongs
    // to the week beginning Monday 17 August.
    const week = workWeek(0, new Date(2026, 7, 23));

    expect(formatDayMonth(week.monday)).toBe('17/08');
  });

  it('covers exactly the five working days', () => {
    const week = workWeek(0, new Date(2026, 7, 19));

    expect(week.days).toHaveLength(5);
    expect(week.days.map((day) => day.getDay())).toEqual([1, 2, 3, 4, 5]);
  });

  it('steps whole weeks in both directions', () => {
    const reference = new Date(2026, 7, 19);

    expect(formatDayMonth(workWeek(-1, reference).monday)).toBe('10/08');
    expect(formatDayMonth(workWeek(1, reference).monday)).toBe('24/08');
  });

  it('crosses a month boundary without losing a day', () => {
    const week = workWeek(0, new Date(2026, 7, 31));

    expect(formatDayMonth(week.monday)).toBe('31/08');
    expect(formatDayMonth(week.friday)).toBe('04/09');
  });
});

describe('isoWeekNumber', () => {
  it('numbers a mid-year week', () => {
    expect(isoWeekNumber(new Date(2026, 7, 17))).toBe(34);
  });

  it('puts 1 January 2027 in week 53 of 2026, per ISO 8601', () => {
    // The year boundary is where week numbering gets argued about. ISO says week 1 is the one containing the
    // first Thursday, which puts this Friday in the previous year's last week — and the pager must agree with
    // whatever the finance team's calendar says.
    expect(isoWeekNumber(new Date(2027, 0, 1))).toBe(53);
  });

  it('numbers the first full week of a year', () => {
    expect(isoWeekNumber(new Date(2026, 0, 5))).toBe(2);
  });
});

describe('startOfWeek', () => {
  it('strips the time of day', () => {
    const monday = startOfWeek(new Date(2026, 7, 19, 14, 37, 12));

    expect(monday.getHours()).toBe(0);
    expect(monday.getMinutes()).toBe(0);
    expect(monday.getSeconds()).toBe(0);
  });
});
