/**
 * The shape of a working day, as the department defines it.
 *
 * Mirrors `WorkingDay` in `Cracra.Modules.Activities.Contracts`. It arrives on the weekly summary rather than from
 * an endpoint of its own, because it answers the same question that payload already answers: what this department
 * expects of this person's week.
 */
export interface WorkingDay {
  /** `HH:mm`, as the server serialises `TimeOnly`. */
  readonly dayStart: string;
  readonly dayEnd: string;
  readonly morningStart: string;
  readonly morningEnd: string;
  readonly afternoonStart: string;
  readonly afternoonEnd: string;
}

/**
 * What a day looks like where the department has said nothing.
 *
 * Kept in step with `WorkingDayPolicy.Default` on the server. Duplicated rather than fetched, because the board
 * has to draw an axis before the first summary comes back and an axis that jumps once the response lands is worse
 * than one that is briefly generic.
 */
export const DEFAULT_WORKING_DAY: WorkingDay = {
  dayStart: '06:00',
  dayEnd: '20:00',
  morningStart: '09:00',
  morningEnd: '13:00',
  afternoonStart: '14:00',
  afternoonEnd: '18:00',
};

/** `"09:30"` → 570. The unit every comparison here is done in, so nothing compares strings and hopes. */
export function minutesOf(time: string): number {
  const [hours, minutes] = time.split(':');

  return Number(hours ?? 0) * 60 + Number(minutes ?? 0);
}

/** 570 → `"09:30"`. */
export function timeOf(minutes: number): string {
  const clamped = Math.max(0, Math.min(24 * 60, Math.round(minutes)));

  return `${pad(Math.floor(clamped / 60))}:${pad(clamped % 60)}`;
}

/** The hours between two `HH:mm`, as the form's own unit. */
export function hoursBetween(from: string, to: string): number {
  return (minutesOf(to) - minutesOf(from)) / 60;
}

/**
 * The named sessions, in the order they run.
 *
 * A list rather than three exported constants so the quick-add's segmented control can render whatever the
 * department configured by iterating, instead of naming each one and drifting when a fourth appears.
 */
export type SessionId = 'morning' | 'afternoon' | 'day';

export interface DaySession {
  readonly id: SessionId;
  readonly labelKey: string;
  readonly start: string;
  readonly end: string;
}

/**
 * The three presses that cover almost every entry.
 *
 * `day` runs from the start of the morning to the end of the afternoon, not from `dayStart` to `dayEnd`. Those two
 * are the outer bound of when work may be recorded — a department whose day is drawn 06:00–20:00 does not mean a
 * fourteen-hour timesheet — so a shortcut built on them would propose a duration nobody could press. The caller
 * caps the result at the daily target on top of that.
 */
export function sessionsOf(day: WorkingDay): readonly DaySession[] {
  return [
    {
      id: 'morning',
      labelKey: 'activity.sessionMorning',
      start: day.morningStart,
      end: day.morningEnd,
    },
    {
      id: 'afternoon',
      labelKey: 'activity.sessionAfternoon',
      start: day.afternoonStart,
      end: day.afternoonEnd,
    },
    { id: 'day', labelKey: 'activity.sessionDay', start: day.morningStart, end: day.afternoonEnd },
  ];
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}
