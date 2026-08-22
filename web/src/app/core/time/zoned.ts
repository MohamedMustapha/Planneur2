/**
 * Reading and writing instants in somebody else's time zone.
 *
 * The platform stores instants — the API speaks `DateTimeOffset` and the client sends ISO strings — but a
 * timesheet is not written in instants. "I worked Tuesday morning" is a statement about a wall clock, and whose
 * wall clock it is has to be the person's own rather than whichever machine the browser happens to be running on.
 * A colleague opening the same board from another country must see the same entries at the same hours.
 *
 * Built on `Intl` rather than a date library, because the app carries none and this needs four functions. The
 * technique is the standard one: ask `Intl` what the clock reads in a zone at a given instant, and use the
 * difference to move between the two.
 */

/** A `Date` whose *local* fields spell out the wall clock of some other zone. */
export type WallClock = Date;

const PARTS = new Map<string, Intl.DateTimeFormat>();

/**
 * The instant, as that zone's clock reads it.
 *
 * The returned `Date` is a lie about the instant and a truth about the clock face: its `getHours()` is the hour
 * the person would see. That is exactly what a calendar widget wants, since every such widget lays events out by
 * local fields. Never send one of these to the server — {@link fromWallClock} is the way back.
 */
export function toWallClock(instant: Date | string, zone: string): WallClock {
  const date = typeof instant === 'string' ? new Date(instant) : instant;

  if (Number.isNaN(date.getTime())) {
    return date;
  }

  const parts = readParts(date, zone);

  return new Date(
    parts.year,
    parts.month - 1,
    parts.day,
    parts.hour,
    parts.minute,
    parts.second,
    date.getMilliseconds(),
  );
}

/**
 * The true instant behind a wall clock reading.
 *
 * Two passes, and the second is not paranoia: the offset used to undo the first guess is the offset at the
 * *guessed* instant, which is the wrong one for the hour either side of a DST change. Re-reading the clock after
 * correcting once settles it — the same fixed-point trick every timezone library uses internally.
 */
export function fromWallClock(wall: WallClock, zone: string): Date {
  if (Number.isNaN(wall.getTime())) {
    return wall;
  }

  const target = Date.UTC(
    wall.getFullYear(),
    wall.getMonth(),
    wall.getDate(),
    wall.getHours(),
    wall.getMinutes(),
    wall.getSeconds(),
    wall.getMilliseconds(),
  );

  let instant = new Date(target - offsetAt(new Date(target), zone));
  instant = new Date(target - offsetAt(instant, zone));

  return instant;
}

/** `2026-08-22` — the calendar day this instant falls on, in that zone. */
export function zonedDay(instant: Date | string, zone: string): string {
  const wall = toWallClock(instant, zone);

  return `${wall.getFullYear()}-${pad(wall.getMonth() + 1)}-${pad(wall.getDate())}`;
}

/** `09:30` — the time of day this instant falls at, in that zone. */
export function zonedTime(instant: Date | string, zone: string): string {
  const wall = toWallClock(instant, zone);

  return `${pad(wall.getHours())}:${pad(wall.getMinutes())}`;
}

/**
 * The instant at which a given day and time read on that zone's clock.
 *
 * The pairing to {@link zonedDay} and {@link zonedTime}: what a form collects as "2026-08-22" and "09:00" becomes
 * the moment those two things were true where the person is.
 */
export function instantOf(day: string, time: string, zone: string): Date {
  const [year, month, date] = day.split('-').map(Number);
  const [hour, minute] = time.split(':').map(Number);

  return fromWallClock(
    new Date(year ?? 0, (month ?? 1) - 1, date ?? 1, hour ?? 0, minute ?? 0, 0, 0),
    zone,
  );
}

/** The zone this browser is in — the honest default before anybody has chosen one. */
export function browserZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
}

/** How far ahead of UTC the zone is at that instant, in milliseconds. */
function offsetAt(instant: Date, zone: string): number {
  const parts = readParts(instant, zone);

  const asUtc = Date.UTC(
    parts.year,
    parts.month - 1,
    parts.day,
    parts.hour,
    parts.minute,
    parts.second,
    instant.getMilliseconds(),
  );

  return asUtc - instant.getTime();
}

interface ClockParts {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
  second: number;
}

function readParts(instant: Date, zone: string): ClockParts {
  const parts = formatterFor(zone).formatToParts(instant);
  const read = (type: Intl.DateTimeFormatPartTypes): number =>
    Number(parts.find((part) => part.type === type)?.value ?? 0);

  // `hourCycle: 'h23'` below is what keeps midnight at 0 rather than at 24, which some locales format instead.
  return {
    year: read('year'),
    month: read('month'),
    day: read('day'),
    hour: read('hour'),
    minute: read('minute'),
    second: read('second'),
  };
}

/**
 * One formatter per zone, kept.
 *
 * Constructing an `Intl.DateTimeFormat` is expensive enough to matter here: a week of entries goes through this
 * on every render of the board, and building a fresh formatter each time was measurably the slowest part of it.
 */
function formatterFor(zone: string): Intl.DateTimeFormat {
  let formatter = PARTS.get(zone);

  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-GB', {
      timeZone: zone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });

    PARTS.set(zone, formatter);
  }

  return formatter;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}
