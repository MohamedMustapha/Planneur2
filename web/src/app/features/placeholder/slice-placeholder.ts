import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ScopeSelector } from '../../shared/ui/scope-selector/scope-selector';

export interface SlicePlaceholderData {
  /** Transloco key for the page title. */
  readonly titleKey: string;
  /** Which slice delivers this screen, e.g. "S4". Shown to the user, not just left in a comment. */
  readonly slice: string;
  readonly descriptionKey: string;
}

/**
 * Stands in for a screen a later slice delivers.
 *
 * It names the slice out loud. The shell is navigable from day one, and an internal user clicking "Portefeuille"
 * gets told what is coming rather than a blank panel that reads as a bug — which is also what keeps the S0
 * acceptance walkthrough honest about what does and does not exist yet.
 */
@Component({
  selector: 'app-slice-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader, ScopeSelector],
  templateUrl: './slice-placeholder.html',
  styleUrl: './slice-placeholder.scss',
})
export class SlicePlaceholder {
  private readonly route = inject(ActivatedRoute);

  private readonly routeData = toSignal(this.route.data, { initialValue: this.route.snapshot.data });

  protected readonly data = computed(() => this.routeData() as unknown as SlicePlaceholderData);
}
