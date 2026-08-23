import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { FocusStore } from '../../core/focus/focus.store';
import { LayoutStore } from '../../core/layout/layout.store';
import { NAVIGATION } from '../../core/navigation/navigation';
import { AccessStore } from '../../core/access/access.store';
import { CapabilityStore } from '../../core/capabilities/capability.store';

@Component({
  selector: 'app-nav-rail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, TranslocoDirective],
  templateUrl: './nav-rail.html',
  styleUrl: './nav-rail.scss',
})
export class NavRail {
  protected readonly layout = inject(LayoutStore);
  protected readonly focus = inject(FocusStore);
  private readonly access = inject(AccessStore);
  private readonly capabilities = inject(CapabilityStore);

  /**
   * Entries the viewer can act on. Recomputed from the session signal, so a role arriving late (the session
   * resource resolves after first paint) fills the rail in without a reload.
   */
  protected readonly items = computed(() => {
    // Effective roles, not the token's: an override granted a minute ago should show its screens without waiting
    // for the access token to expire.
    const roles = this.access.roles();

    return NAVIGATION.filter(
      (item) =>
        (!item.requiresAnyRole || item.requiresAnyRole.some((role) => roles.includes(role))) &&
        // v2 §10.3. ANDed with the role check because they hide for different reasons and both are real: the
        // role says this viewer cannot use it, the capability says their branch does not do it.
        (!item.requiresCapability || this.capabilities.allows(item.requiresCapability)),
    );
  });

  /**
   * Whether the rail shows labels.
   *
   * Focus mode wins over the person's own collapse preference rather than overwriting it (§02.2: "collapses the
   * left nav to icons"). Leaving the toggle's stored value alone is what makes leaving Focus mode restore the
   * rail they had, instead of the one Focus mode left behind.
   */
  protected readonly showLabels = computed(() => this.layout.railExpanded() && !this.focus.active());
}
