import { endDateFor } from './portfolio.store';

/**
 * The client's copy of the preset arithmetic.
 *
 * It exists only to pre-fill the field as a preset is picked, and the server recomputes it — but a client that
 * shows one date and stores another is worse than one that shows nothing, so the two must agree. These cases
 * mirror `IterationTests` on the server exactly.
 */
describe('endDateFor', () => {
  it('makes one week seven days inclusive', () => {
    // Monday to the following Sunday. Eight days would make consecutive weekly iterations overlap.
    expect(endDateFor('oneweek', '2026-08-17')).toBe('2026-08-23');
  });

  it('makes two weeks fourteen days inclusive', () => {
    expect(endDateFor('twoweeks', '2026-08-17')).toBe('2026-08-30');
  });

  it('makes one month a calendar month', () => {
    expect(endDateFor('onemonth', '2026-01-01')).toBe('2026-01-31');
  });

  it('clamps a month that would overflow', () => {
    // 31 January plus a month is the end of February, not 3 March — the same clamping AddMonths gives the server.
    expect(endDateFor('onemonth', '2026-01-31')).toBe('2026-02-27');
  });

  it('leaves a custom length to the user', () => {
    expect(endDateFor('custom', '2026-08-17')).toBe('2026-08-17');
  });
});
