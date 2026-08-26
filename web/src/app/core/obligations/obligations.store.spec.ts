import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionStore } from '../session/session.store';
import { ObligationsStore } from './obligations.store';

interface ObligationDto {
  readonly id: string;
  readonly key: string;
  readonly params: Record<string, string>;
  readonly severity: string;
}

const overTarget: ObligationDto = {
  id: 'over-target',
  key: 'obligations.overTarget',
  params: { hours: '5' },
  severity: 'warning',
};

const overdue: ObligationDto = {
  id: 'overdue-actions',
  key: 'obligations.overdueActions',
  params: { count: '2' },
  severity: 'danger',
};

/**
 * The one thing allowed to pierce Focus mode.
 *
 * All three sources are the server's answer now, so what is worth pinning here is the client half: that a
 * dismissal hides a chip without settling the fact behind it, and that the banner picks the most severe one.
 */
describe('ObligationsStore', () => {
  async function storeWith(rows: readonly ObligationDto[]): Promise<ObligationsStore> {
    TestBed.resetTestingModule();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: { isAuthenticated: signal(true) } },
      ],
    });

    const store = TestBed.inject(ObligationsStore);

    TestBed.tick();
    TestBed.inject(HttpTestingController).expectOne('/api/guidance/obligations').flush(rows);

    // The fetch resolves on a microtask, so the signal is only readable after one has run.
    await Promise.resolve();

    return store;
  }

  it('finds nothing to say about a viewer who owes nothing', async () => {
    const store = await storeWith([]);

    expect(store.obligations()).toEqual([]);
    expect(store.hasAny()).toBe(false);
    expect(store.top()).toBeNull();
  });

  it('raises a chip for each obligation the server reports', async () => {
    const store = await storeWith([overTarget]);

    expect(store.hasAny()).toBe(true);
    expect(store.obligations()[0].id).toBe('over-target');
    expect(store.obligations()[0].severity).toBe('warning');
  });

  it('labels through a key rather than a sentence', async () => {
    const store = await storeWith([overTarget]);

    // A chip built from a pre-rendered string would keep the language it was computed in and stop re-labelling on
    // a switch, which is the one thing every other label in the shell gets right.
    expect(store.top()?.labelKey).toBe('obligations.overTarget');
    expect(store.top()?.params).toEqual({ hours: '5' });
  });

  it('gives the banner the most severe one', async () => {
    const store = await storeWith([overTarget, overdue]);

    // The Focus banner has room for one. An overdue commitment outranks a long week.
    expect(store.top()?.id).toBe('overdue-actions');
  });

  it('hides a chip the viewer waved away', async () => {
    const store = await storeWith([overTarget]);

    store.dismiss('over-target');

    expect(store.obligations()).toEqual([]);
    expect(store.hasAny()).toBe(false);
  });

  it('ignores a second dismissal of the same chip', async () => {
    const store = await storeWith([overTarget]);

    store.dismiss('over-target');
    store.dismiss('over-target');

    expect(store.obligations()).toEqual([]);
  });

  it('leaves other obligations alone when one is dismissed', async () => {
    const store = await storeWith([overTarget]);

    store.dismiss('something-else-entirely');

    expect(store.hasAny()).toBe(true);
  });

  it('keeps the underlying fact rather than settling it', async () => {
    const store = await storeWith([overTarget]);

    store.dismiss('over-target');

    // Dismissal is session-scoped and hides the chip only. Persisting it would let somebody silence a genuine
    // obligation permanently, which is the opposite of what one is for — so a fresh store still raises it.
    expect(store.hasAny()).toBe(false);
    expect((await storeWith([overTarget])).hasAny()).toBe(true);
  });
});
