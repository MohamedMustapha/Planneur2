import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CapabilityStore } from '../../core/capabilities/capability.store';
import { NODE_CAPABILITIES } from '../../core/directory/directory.models';

/**
 * The tab strip shared by the two administration screens.
 *
 * They sit behind one nav entry because the design fixes the rail at ten sections, and because "configure this
 * department" and "who may see what" are the same job from an administrator's point of view.
 */
@Component({
  selector: 'app-settings-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, TranslocoDirective],
  templateUrl: './settings-tabs.html',
  styles: `
    .tabs {
      padding: 0 var(--space-4);
      border-bottom: none;
    }

    .tab {
      text-decoration: none;
    }
  `,
})
export class SettingsTabs {
  private readonly capabilities = inject(CapabilityStore);

  /**
   * v2 §10.3: a branch without the integrations capability does not get the tab at all.
   *
   * Removed rather than disabled, and removed here rather than inside the screen, because a tab that navigates to
   * an explanation of why you may not use it is still a tab telling someone about a feature that is not part of
   * their work. The route stays reachable by URL — the API is what refuses, and it refuses the same way for
   * everyone who should not be there.
   */
  protected readonly integrations = this.capabilities.allowsSignal(NODE_CAPABILITIES.integrations);
}
