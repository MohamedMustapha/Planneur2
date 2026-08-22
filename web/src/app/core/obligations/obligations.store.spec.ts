import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivitiesStore } from '../activities/activities.store';
import { ObligationsStore } from './obligations.store';

/**
 * The one thing allowed to pierce Focus mode.
 *
 * An obligation is something the viewer owes somebody, so the failure worth guarding against is the quiet one: a
 * chip that stops appearing, or a dismissal that outlives the fact it waved away. Both would leave Focus mode
 * concealing exactly what §02.2 says it may never conceal.
 */
describe('ObligationsStore', () => {
  /** Resets first, so a scenario may stand up a second store to ask what a fresh session would see. */
  function storeWith(status: { overTarget: boolean; overtime?: number }): ObligationsStore {
    TestBed.resetTestingModule();

    const activities = {
      isOverTarget: signal(status.overTarget),
      overtime: signal(status.overtime ?? 0),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: ActivitiesStore, useValue: activities }],
    });

    return TestBed.inject(ObligationsStore);
  }

  it('finds nothing to say about a week inside its target', () => {
    const store = storeWith({ overTarget: false });

    expect(store.obligations()).toEqual([]);
    expect(store.hasAny()).toBe(false);
    expect(store.top()).toBeNull();
  });

  it('raises a chip for a week over target', () => {
    const store = storeWith({ overTarget: true, overtime: 5 });

    const obligation = store.obligations()[0];

    expect(store.hasAny()).toBe(true);
    expect(obligation.id).toBe('week-over-target');
    expect(obligation.severity).toBe('warning');
  });

  it('labels through a key rather than a sentence', () => {
    const store = storeWith({ overTarget: true, overtime: 5 });

    // A chip built from a pre-rendered string would keep the language it was computed in and stop re-labelling on
    // a switch, which is the one thing every other label in the shell gets right.
    expect(store.top()?.labelKey).toBe('obligations.weekOverTarget');
    expect(store.top()?.params).toEqual({ hours: 5 });
  });

  it('recomputes when the week does', () => {
    TestBed.resetTestingModule();

    const activities = { isOverTarget: signal(false), overtime: signal(0) };

    TestBed.configureTestingModule({
      providers: [{ provide: ActivitiesStore, useValue: activities }],
    });

    const store = TestBed.inject(ObligationsStore);

    expect(store.hasAny()).toBe(false);

    activities.isOverTarget.set(true);
    activities.overtime.set(2);

    // Derived, not pushed: logging the hour that tips the week over has to raise the chip without anything
    // remembering to tell this store about it.
    expect(store.hasAny()).toBe(true);
    expect(store.top()?.params).toEqual({ hours: 2 });
  });

  it('hides a chip the viewer waved away', () => {
    const store = storeWith({ overTarget: true, overtime: 5 });

    store.dismiss('week-over-target');

    expect(store.obligations()).toEqual([]);
    expect(store.hasAny()).toBe(false);
  });

  it('ignores a second dismissal of the same chip', () => {
    const store = storeWith({ overTarget: true, overtime: 5 });

    store.dismiss('week-over-target');
    store.dismiss('week-over-target');

    expect(store.obligations()).toEqual([]);
  });

  it('leaves other obligations alone when one is dismissed', () => {
    const store = storeWith({ overTarget: true, overtime: 5 });

    store.dismiss('something-else-entirely');

    expect(store.hasAny()).toBe(true);
  });

  it('keeps the underlying fact rather than settling it', () => {
    const store = storeWith({ overTarget: true, overtime: 5 });

    store.dismiss('week-over-target');

    // Dismissal is session-scoped and hides the chip only. Persisting it would let somebody silence a genuine
    // overrun permanently, which is the opposite of what an obligation is for — so a fresh store still raises it.
    expect(storeWith({ overTarget: true, overtime: 5 }).hasAny()).toBe(true);
  });
});
