import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CapabilityStore } from '../capabilities/capability.store';
import { SessionStore } from '../session/session.store';
import { NavigationStore, ShellNavigation } from './navigation.store';

const headOverChildren: ShellNavigation = {
  position: 'head-branch',
  landingId: 'node',
  focusId: 'node',
  primary: ['node', 'portfolio', 'finance', 'reports'],
  secondary: ['board', 'kudos'],
};

/**
 * The client half of the nav: it renders the server's decision and does not take one of its own.
 *
 * The failure worth pinning is the tempting one — a client that "helpfully" adds an entry back, or that renders
 * something before the server has answered and then rearranges the rail under somebody mid-click.
 */
describe('NavigationStore', () => {
  async function storeWith(
    shell: ShellNavigation | null,
    capabilities: Record<string, boolean> = {},
  ): Promise<NavigationStore> {
    TestBed.resetTestingModule();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: { isAuthenticated: signal(true) } },
        {
          provide: CapabilityStore,
          useValue: { allows: (code: string) => capabilities[code] ?? true },
        },
      ],
    });

    const store = TestBed.inject(NavigationStore);
    const http = TestBed.inject(HttpTestingController);

    TestBed.tick();

    if (shell) {
      http.expectOne('/api/guidance/navigation').flush(shell);
    } else {
      http.expectOne('/api/guidance/navigation').error(new ProgressEvent('offline'));
    }

    // The fetch resolves on a microtask, so the signals are only readable after one has run.
    await Promise.resolve();

    return store;
  }

  it('renders the sections the server put in each group', async () => {
    const store = await storeWith(headOverChildren);

    expect(store.primary().map((item) => item.id)).toEqual([
      'node',
      'portfolio',
      'finance',
      'reports',
    ]);
    expect(store.secondary().map((item) => item.id)).toEqual(['board', 'kudos']);
  });

  it('turns the landing and focus sections into routes', async () => {
    const store = await storeWith(headOverChildren);

    expect(store.landingRoute()).toBe('/node');
    expect(store.focusRoute()).toBe('/node');
  });

  it('shows a member’s shell until the server answers', async () => {
    const store = await storeWith(null);

    // The narrowest thing to be wrong about. Rendering a head's rail and then taking half of it away is how a
    // person clicks Budget and lands somewhere else.
    expect(store.position()).toBe('member');
    expect(store.primary().map((item) => item.id)).toEqual(['board', 'problems', 'kudos']);
  });

  it('drops a section the viewer’s branch does not do', async () => {
    const store = await storeWith(headOverChildren, { budget: false });

    // §10.3: absent, not disabled. A head entitled to read budgets still has none in a branch that carries none.
    expect(store.primary().map((item) => item.id)).not.toContain('finance');
    expect(store.primary().map((item) => item.id)).toContain('node');
  });

  it('ignores a section id it has no route for', async () => {
    const store = await storeWith({ ...headOverChildren, primary: ['node', 'not-a-section'] });

    // A server that grows a section before the client does should lose the entry, not throw on every render.
    expect(store.primary().map((item) => item.id)).toEqual(['node']);
  });

  it('never names a level in a route', async () => {
    const store = await storeWith(headOverChildren);

    for (const item of [...store.primary(), ...store.secondary()]) {
      expect(item.route).not.toMatch(/unit|department|bureau|service/);
    }
  });
});
