import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, Router } from '@angular/router';
import { Subject } from 'rxjs';
import { AccessStore } from '../access/access.store';
import { FocusStore, focusDefaultFor, focusRouteFor, isPiercedBy } from './focus.store';

/**
 * The three decisions Focus mode makes on its own.
 *
 * Everything else about the mode is presentation — which elements a template draws — and is covered by the E2E
 * pass. These three are rules, and a rule that drifts silently is the reason a member finds themselves landing on
 * a portfolio they cannot read.
 */
describe('focusRouteFor', () => {
  it('lands a member on their own week', () => {
    expect(focusRouteFor(['member'])).toBe('/board');
  });

  it('lands a unit head on their team board', () => {
    expect(focusRouteFor(['member', 'unit-head'])).toBe('/team');
  });

  it('prefers the widest responsibility when somebody wears two hats', () => {
    // Being a PMO is the job; being a member is how the payroll describes you. Opening on the personal week would
    // make the first click of every session "navigate away from here".
    expect(focusRouteFor(['member', 'unit-head', 'pmo'])).toBe('/portfolio');
  });

  it('falls back to the board for a viewer with no contextual role yet', () => {
    // whoami has not resolved. The personal week is the one screen everybody is entitled to.
    expect(focusRouteFor([])).toBe('/board');
  });
});

describe('focusDefaultFor', () => {
  it('is on for a member', () => {
    expect(focusDefaultFor(['member'])).toBe(true);
  });

  it('is off for anyone whose day is comparison', () => {
    expect(focusDefaultFor(['member', 'dept-head'])).toBe(false);
    expect(focusDefaultFor(['pmo'])).toBe(false);
    expect(focusDefaultFor(['po'])).toBe(false);
  });
});

describe('isPiercedBy', () => {
  it('does not pierce on the focus route itself', () => {
    expect(isPiercedBy('/board', '/board')).toBe(false);
  });

  it('ignores the query string', () => {
    expect(isPiercedBy('/board?week=3', '/board')).toBe(false);
  });

  it('treats a detail route under the target as still being there', () => {
    expect(isPiercedBy('/projects/42', '/projects')).toBe(false);
  });

  it('pierces on a deep link elsewhere', () => {
    expect(isPiercedBy('/finance', '/board')).toBe(true);
  });
});

/**
 * The store around those three rules.
 *
 * What is worth pinning here is the state the rules are read through: that "never chosen" survives as null so the
 * role default keeps applying, that the cache is a cache rather than the record of intent, and that a pierced
 * navigation resolves itself by arithmetic instead of by something remembering to reset a flag.
 */
describe('FocusStore', () => {
  function storeFor(
    roles: readonly string[],
    options: { url?: string } = {},
  ): { store: FocusStore; navigate: (url: string) => void; navigated: string[] } {
    TestBed.resetTestingModule();

    const events = new Subject<NavigationEnd>();
    const navigated: string[] = [];
    const router = {
      events,
      url: options.url ?? '/board',
      navigateByUrl: (url: string) => {
        navigated.push(url);

        return Promise.resolve(true);
      },
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AccessStore, useValue: { roles: signal(roles) } },
        { provide: Router, useValue: router },
      ],
    });

    const store = TestBed.inject(FocusStore);

    return {
      store,
      navigate: (url: string) => {
        router.url = url;
        events.next(new NavigationEnd(1, url, url));
      },
      navigated,
    };
  }

  beforeEach(() => localStorage.clear());

  it('applies the role default while nobody has chosen', () => {
    expect(storeFor(['member']).store.enabled()).toBe(true);
    expect(storeFor(['member', 'dept-head']).store.enabled()).toBe(false);
  });

  it('lets a choice override the default in either direction', () => {
    const member = storeFor(['member']).store;

    member.choose(false);
    expect(member.enabled()).toBe(false);

    const head = storeFor(['pmo']).store;

    head.choose(true);
    expect(head.enabled()).toBe(true);
  });

  it('toggles from whatever is currently in effect', () => {
    // Including from a default nobody has overridden yet — the first press of the toggle has to mean "not that"
    // rather than "false", or a member's first press appears to do nothing.
    const { store } = storeFor(['member']);

    store.toggle();

    expect(store.enabled()).toBe(false);
    expect(store.chosen).toBe(false);
  });

  it('reports no choice until one is made', () => {
    // Null is what the preferences PUT has to carry for "never chosen"; a false here would opt every member out
    // of the mode the spec wants them in, permanently and invisibly.
    expect(storeFor(['member']).store.chosen).toBeNull();
  });

  it('renders a cold load from the cached preference', () => {
    localStorage.setItem('cracra.focus', 'false');

    // The whole point of the cache: a member who turned Focus mode off should not see the stripped shell flash
    // past on every reload while `/me` is still in flight.
    expect(storeFor(['member']).store.enabled()).toBe(false);
  });

  it('ignores a cache that says nothing', () => {
    localStorage.setItem('cracra.focus', 'perhaps');

    expect(storeFor(['member']).store.enabled()).toBe(true);
  });

  it('writes the cache as the choice changes', () => {
    const { store } = storeFor(['member']);

    store.choose(false);
    TestBed.tick();

    expect(localStorage.getItem('cracra.focus')).toBe('false');
  });

  it('clears the cache when the choice is handed back', () => {
    const { store } = storeFor(['member']);

    store.choose(false);
    TestBed.tick();

    store.adoptProfileFocusMode(null);
    TestBed.tick();

    // A cached "false" outliving the preference would keep answering for a person who has none, and the role
    // default would never apply again on this machine.
    expect(localStorage.getItem('cracra.focus')).toBeNull();
    expect(store.enabled()).toBe(true);
  });

  it('takes the server’s answer as the answer', () => {
    const { store } = storeFor(['member']);

    store.choose(true);
    store.adoptProfileFocusMode(false);

    // Unconditional, unlike the theme: theme is a property of the machine you are sitting at, Focus mode is a
    // statement about how you work.
    expect(store.enabled()).toBe(false);
  });

  it('hangs the mode off the document element', () => {
    const { store } = storeFor(['member']);

    TestBed.tick();
    expect(document.documentElement.getAttribute('data-focus')).toBe('on');

    store.choose(false);
    TestBed.tick();
    expect(document.documentElement.getAttribute('data-focus')).toBe('off');
  });

  it('renders a deep link in full mode without leaving the mode', () => {
    const { store, navigate } = storeFor(['member']);

    navigate('/finance');

    // Pierced, not switched off: the toggle still reads "on", and going back restores the stripped shell without
    // the person having to turn anything on again.
    expect(store.pierced()).toBe(true);
    expect(store.active()).toBe(false);
    expect(store.enabled()).toBe(true);
  });

  it('un-pierces by arithmetic when the viewer comes back', () => {
    const { store, navigate } = storeFor(['member']);

    navigate('/finance');
    navigate('/board');

    // Derived from the URL rather than set by a guard, so there is nothing to remember to reset — which is how a
    // person ends up stuck in full mode with no way back short of a reload.
    expect(store.pierced()).toBe(false);
    expect(store.active()).toBe(true);
  });

  it('cannot be pierced while the mode is off', () => {
    const { store, navigate } = storeFor(['member']);

    store.choose(false);
    navigate('/finance');

    expect(store.pierced()).toBe(false);
    expect(store.active()).toBe(false);
  });

  it('follows the viewer’s own focus route rather than the board', () => {
    const { store, navigate } = storeFor(['member', 'pmo'], { url: '/portfolio' });

    store.choose(true);

    expect(store.focusRoute()).toBe('/portfolio');
    expect(store.pierced()).toBe(false);

    navigate('/board');

    // A PMO's own week is a deep link away from the portfolio, the same way the portfolio is for a member.
    expect(store.pierced()).toBe(true);
  });

  it('sends the viewer back to their focus screen', () => {
    const { store, navigated } = storeFor(['member', 'unit-head']);

    store.returnToFocus();

    expect(navigated).toEqual(['/team']);
  });
});
