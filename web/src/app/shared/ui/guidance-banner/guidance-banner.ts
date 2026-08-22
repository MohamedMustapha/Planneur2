import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ObligationsStore } from '../../../core/obligations/obligations.store';

/**
 * The next-best-action banner — v2 §02.5, item 2.
 *
 * Every landing page computes **one** suggested action from real state and shows it as a sentence plus a primary
 * button. One, not a list: a page that suggests four things has suggested nothing, and the whole point of the
 * guidance system is that "what do I do here" has an answer rather than a menu.
 *
 * The banner is also where the obligation chip surfaces (§02.2), because an obligation and a next-best-action are
 * the same kind of statement — "here is the thing you owe" — and splitting them across two strips means the viewer
 * has to look in two places to find out whether they are done.
 *
 * Deliberately dumb: the *choice* of action is the page's, since only the page knows its own state. This component
 * decides how that choice looks and nothing else. When `GET /api/guidance/next-action` lands, the pages start
 * reading their message from the server and this component does not change at all.
 */
@Component({
  selector: 'app-guidance-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective],
  templateUrl: './guidance-banner.html',
  styleUrl: './guidance-banner.scss',
})
export class GuidanceBanner {
  protected readonly obligations = inject(ObligationsStore);

  /** The sentence. Already translated by the caller, which is the one that holds the numbers in it. */
  readonly message = input.required<string>();

  /** Optional second line — the reason, where the sentence alone does not carry it. */
  readonly detail = input<string>('');

  /** Label of the single primary action, or empty for a banner that only informs. */
  readonly actionLabel = input<string>('');

  /** Whether to draw the obligation chip. Off on screens that already show obligations some other way. */
  readonly showObligation = input(true);

  readonly acted = output<void>();
}
