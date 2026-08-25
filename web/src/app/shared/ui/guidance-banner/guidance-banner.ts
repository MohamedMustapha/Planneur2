import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { GuidanceStore } from '../../../core/guidance/guidance.store';
import { ObligationsStore } from '../../../core/obligations/obligations.store';

/**
 * The next-best-action banner (v2 §02.5). One action, not a list, and the obligation chip beside it — both are
 * the same kind of statement about what the viewer owes.
 */
@Component({
  selector: 'app-guidance-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective],
  templateUrl: './guidance-banner.html',
  styleUrl: './guidance-banner.scss',
})
export class GuidanceBanner {
  private readonly router = inject(Router);

  protected readonly obligations = inject(ObligationsStore);
  protected readonly guidance = inject(GuidanceStore);

  /** Whether to draw the obligation chip. Off on screens that already show obligations some other way. */
  readonly showObligation = input(true);

  protected act(): void {
    void this.router.navigateByUrl(this.guidance.route());
  }
}
