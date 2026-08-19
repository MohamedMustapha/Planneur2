import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LayoutStore } from '../../core/layout/layout.store';
import { NAVIGATION } from '../../core/navigation/navigation';
import { SessionStore } from '../../core/session/session.store';

@Component({
  selector: 'app-nav-rail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, TranslocoDirective],
  templateUrl: './nav-rail.html',
  styleUrl: './nav-rail.scss',
})
export class NavRail {
  protected readonly layout = inject(LayoutStore);
  private readonly session = inject(SessionStore);

  /**
   * Entries the viewer can act on. Recomputed from the session signal, so a role arriving late (the session
   * resource resolves after first paint) fills the rail in without a reload.
   */
  protected readonly items = computed(() => {
    const roles = this.session.roles();

    return NAVIGATION.filter(
      (item) => !item.requiresAnyRole || item.requiresAnyRole.some((role) => roles.includes(role)),
    );
  });
}
