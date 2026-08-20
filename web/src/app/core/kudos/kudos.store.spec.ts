import { counterScopeFor, initialsOf } from './kudos.store';

/**
 * The two pure decisions the kudos client makes on its own.
 *
 * Everything else it renders is decided by the server — the mode, the price, who is eligible — which is the point:
 * these are the only two places the browser could disagree with it, so they are the only two worth pinning.
 */
describe('counterScopeFor', () => {
  it('keeps the department scope', () => {
    expect(counterScopeFor('department')).toBe('department');
  });

  it('counts the unit for a personal wall', () => {
    // A counter is about a group, and a group of one is a number somebody already knows.
    expect(counterScopeFor('me')).toBe('unit');
  });

  it('counts the unit for a unit wall', () => {
    expect(counterScopeFor('unit')).toBe('unit');
  });
});

describe('initialsOf', () => {
  it('takes the first letter of the first two words', () => {
    expect(initialsOf('Camille Villeneuve')).toBe('CV');
  });

  it('stops at two', () => {
    expect(initialsOf('Marie Anne Claire Dupont')).toBe('MA');
  });

  it('copes with a single name', () => {
    expect(initialsOf('Nadia')).toBe('N');
  });

  it('marks somebody the reader may not see', () => {
    // Their kudo is on the wall because the reader is the other party to it. A blank circle would read as a
    // rendering fault rather than as a name this reader is not entitled to.
    expect(initialsOf(null)).toBe('?');
  });
});
