import { computed, inject, Injectable } from '@angular/core';
import { DirectoryStore } from '../directory/directory.store';
import { NodeCapability, NodeProfileSnapshot } from '../directory/directory.models';

/**
 * What this viewer's branch can do (v2 §10.3).
 *
 * The profile arrives with `/api/directory/me`, so the answer is available before the first paint. That timing is
 * the requirement, not an optimisation: a nav that renders everything and removes entries once a second request
 * lands has already shown someone a feature their branch does not have, which is precisely the failure the
 * "absent, not disabled" rule exists to prevent.
 *
 * Nothing here reads `sourceCode`. The store answers "may this branch do X", never "is this branch a delivery
 * one" — an architecture test on the server enforces the same rule there, and the reason is identical: the moment
 * a screen asks which profile it is looking at, profiles have stopped being data.
 */
@Injectable({ providedIn: 'root' })
export class CapabilityStore {
  private readonly directory = inject(DirectoryStore);

  readonly profile = computed<NodeProfileSnapshot | null>(() => this.directory.me()?.profile ?? null);

  /** The profile's label key, for the one place this is legitimately shown: telling an admin what is in force. */
  readonly labelKey = computed(() => this.profile()?.labelKey ?? null);

  /**
   * Whether a capability is on.
   *
   * Two different unknowns both answer `true`, deliberately:
   * no profile in force (nothing has been configured, so nothing should be taken away), and a capability the
   * resolved profile does not mention (the server merges platform defaults in, so a missing key means the default
   * — and the defaults are permissive for the same reason).
   *
   * The load window is the case worth being explicit about. Until `me` resolves, `profile()` is null and every
   * capability reads as on. That is the right way round: a control that appears and then vanishes is a worse
   * experience than one that appears slightly late, but a control that *should* be hidden must never be the one
   * that flashes — which is why the profile ships with the session rather than being fetched separately.
   */
  allows(capability: NodeCapability): boolean {
    const profile = this.profile();

    if (!profile) {
      return true;
    }

    return profile.capabilities[capability] ?? true;
  }

  /** Signal form, for templates and `computed` chains that must re-evaluate when the session resolves. */
  allowsSignal(capability: NodeCapability) {
    return computed(() => this.allows(capability));
  }
}
