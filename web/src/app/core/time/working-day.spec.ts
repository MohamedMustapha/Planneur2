import { describe, expect, it } from 'vitest';
import {
  DEFAULT_WORKING_DAY,
  hoursBetween,
  minutesOf,
  sessionsOf,
  timeOf,
  WorkingDay,
} from './working-day';

describe('working day', () => {
  it('converts between a clock reading and minutes', () => {
    expect(minutesOf('09:30')).toBe(570);
    expect(timeOf(570)).toBe('09:30');
    expect(timeOf(0)).toBe('00:00');
  });

  it('measures a session in the hours an entry records', () => {
    expect(hoursBetween('09:00', '13:00')).toBe(4);
    expect(hoursBetween('14:00', '17:30')).toBe(3.5);
  });

  it('offers the department s own three sessions', () => {
    const sessions = sessionsOf(DEFAULT_WORKING_DAY);

    expect(sessions.map((session) => session.id)).toEqual(['morning', 'afternoon', 'day']);
    expect(sessions[0]).toMatchObject({ start: '09:00', end: '13:00' });
    expect(sessions[1]).toMatchObject({ start: '14:00', end: '18:00' });
  });

  it('spans a full day from the start of the morning to the end of the afternoon', () => {
    // Not dayStart to dayEnd: those are the bounds of when work may be recorded, not a claim that anyone works
    // them end to end. A "full day" that proposed fourteen hours would be a shortcut nobody could press.
    const day = sessionsOf(DEFAULT_WORKING_DAY).find((session) => session.id === 'day');

    expect(day).toMatchObject({ start: '09:00', end: '18:00' });
  });

  it('follows a department that works other hours', () => {
    const early: WorkingDay = {
      dayStart: '06:00',
      dayEnd: '16:00',
      morningStart: '06:30',
      morningEnd: '11:30',
      afternoonStart: '12:00',
      afternoonEnd: '15:00',
    };

    const sessions = sessionsOf(early);

    expect(sessions[0]).toMatchObject({ start: '06:30', end: '11:30' });
    expect(hoursBetween(sessions[1]!.start, sessions[1]!.end)).toBe(3);
  });
});
