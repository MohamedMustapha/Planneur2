import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The page-title row every screen shares: title, purpose line, a slot for the page's own controls, and a slot for
 * the tab strip. Keeping it one component is what makes the boards feel like one application rather than ten
 * screens that happen to share a colour palette.
 *
 * `purpose` is the first of the three mandatory guidance elements (v2 §02.5): one sentence under every page title
 * saying what this page is *for*, and for whom. It replaces `subtitle`, which said who *you* were — information
 * the top bar already carries, and which never once answered the question people actually arrive with.
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-header.html',
  styleUrl: './page-header.scss',
})
export class PageHeader {
  readonly title = input.required<string>();

  /** One sentence: what this page is for. Transloco key `<feature>.page.purpose`, resolved by the caller. */
  readonly purpose = input<string>('');

  /** Context — the person, unit or scope the page is rendered against. Secondary to the purpose, and quieter. */
  readonly subtitle = input<string>('');
}
