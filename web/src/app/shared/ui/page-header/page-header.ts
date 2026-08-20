import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The page-title row every screen shares: title, context line, a slot for the page's own controls, and a slot for
 * the tab strip. Keeping it one component is what makes the boards feel like one application rather than ten
 * screens that happen to share a colour palette.
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './page-header.html',
  styleUrl: './page-header.scss',
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string>('');
}
