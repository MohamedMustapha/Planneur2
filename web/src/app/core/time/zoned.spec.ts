import { describe, expect, it } from 'vitest';
import { fromWallClock, instantOf, toWallClock, zonedDay, zonedTime } from './zoned';

/**
 * The two directions have to be exact inverses, because an entry is written through one and read back through the
 * other. Anything that survives a round trip only approximately shows up as a timesheet whose hours drift.
 *
 * The interesting cases are all boundaries: DST in both directions, a zone offset that is not a whole hour, and
 * midnight — where an off-by-one lands the entry on the wrong calendar day, which is the failure people notice.
 */
describe('zoned', () => {
  it('reads an instant as the clock face of the chosen zone', () => {
    // 09:00 UTC in January is 10:00 in Paris (UTC+1).
    const instant = new Date('2026-01-15T09:00:00Z');

    expect(zonedTime(instant, 'Europe/Paris')).toBe('10:00');
    expect(zonedTime(instant, 'UTC')).toBe('09:00');
    expect(zonedTime(instant, 'America/New_York')).toBe('04:00');
  });

  it('puts an instant on the calendar day the reader is living in', () => {
    // 23:30 in New York is already the next day in Paris. The day is the thing the board groups by, so this is
    // the assertion that keeps an evening entry off tomorrow's row.
    const instant = new Date('2026-08-20T03:30:00Z');

    expect(zonedDay(instant, 'America/New_York')).toBe('2026-08-19');
    expect(zonedDay(instant, 'Europe/Paris')).toBe('2026-08-20');
  });

  it('round-trips an instant through a wall clock unchanged', () => {
    for (const zone of [
      'Europe/Paris',
      'America/New_York',
      'Asia/Kolkata',
      'Pacific/Chatham',
      'UTC',
    ]) {
      const instant = new Date('2026-08-20T13:45:00Z');

      expect(fromWallClock(toWallClock(instant, zone), zone).toISOString()).toBe(
        instant.toISOString(),
      );
    }
  });

  it('round-trips across a half-hour and a three-quarter-hour offset', () => {
    // Kolkata is UTC+05:30 and Chatham is UTC+12:45. A conversion built on whole hours passes every other test in
    // this file and fails these two.
    const instant = new Date('2026-03-10T22:15:00Z');

    for (const zone of ['Asia/Kolkata', 'Pacific/Chatham']) {
      expect(fromWallClock(toWallClock(instant, zone), zone).getTime()).toBe(instant.getTime());
    }
  });

  it('round-trips across the spring-forward boundary', () => {
    // Paris jumps from 02:00 to 03:00 on 29 March 2026. The instants either side must survive intact.
    for (const iso of ['2026-03-29T00:30:00Z', '2026-03-29T01:30:00Z', '2026-03-29T02:30:00Z']) {
      const instant = new Date(iso);

      expect(fromWallClock(toWallClock(instant, 'Europe/Paris'), 'Europe/Paris').getTime()).toBe(
        instant.getTime(),
      );
    }
  });

  it('round-trips across the autumn fall-back boundary', () => {
    // 25 October 2026: Paris reads 02:30 twice. One of the two has to come back, and it must be a real instant
    // rather than a shifted one — this is the case a single-pass offset calculation gets wrong.
    for (const iso of ['2026-10-25T00:30:00Z', '2026-10-25T01:30:00Z', '2026-10-25T02:30:00Z']) {
      const instant = new Date(iso);
      const restored = fromWallClock(toWallClock(instant, 'Europe/Paris'), 'Europe/Paris');

      expect(zonedTime(restored, 'Europe/Paris')).toBe(zonedTime(instant, 'Europe/Paris'));
    }
  });

  it('turns a form s day and time into the instant that reading stands for', () => {
    // What quick-add does on every save: 09:00 in Paris in August is 07:00 UTC.
    expect(instantOf('2026-08-20', '09:00', 'Europe/Paris').toISOString()).toBe(
      '2026-08-20T07:00:00.000Z',
    );
    expect(instantOf('2026-08-20', '09:00', 'America/New_York').toISOString()).toBe(
      '2026-08-20T13:00:00.000Z',
    );
  });

  it('agrees with itself: an instant built from a reading reads back the same', () => {
    for (const zone of ['Europe/Paris', 'America/New_York', 'Asia/Kolkata']) {
      const instant = instantOf('2026-12-01', '08:15', zone);

      expect(zonedDay(instant, zone)).toBe('2026-12-01');
      expect(zonedTime(instant, zone)).toBe('08:15');
    }
  });

  it('leaves an invalid date invalid rather than inventing one', () => {
    expect(Number.isNaN(toWallClock(new Date('nonsense'), 'Europe/Paris').getTime())).toBe(true);
  });
});
