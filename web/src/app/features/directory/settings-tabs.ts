import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

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
export class SettingsTabs {}
