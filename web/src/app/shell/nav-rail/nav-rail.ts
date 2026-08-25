import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { FocusStore } from '../../core/focus/focus.store';
import { LayoutStore } from '../../core/layout/layout.store';
import { NavigationStore } from '../../core/navigation/navigation.store';

/** The rail: the server's primary group, and a "More" disclosure for the rest (v2 §02.1). */
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
  protected readonly navigation = inject(NavigationStore);

  protected readonly moreOpen = signal(false);

  protected readonly primary = computed(() => this.navigation.primary());

  protected readonly secondary = computed(() => this.navigation.secondary());

  /** Focus mode wins over the collapse preference rather than overwriting it, so leaving restores it. */
  protected readonly showLabels = computed(() => this.layout.railExpanded() && !this.focus.active());

  protected toggleMore(): void {
    this.moreOpen.update((open) => !open);
  }
}
