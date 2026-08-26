import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CoachStore } from '../../../core/coach/coach.store';

/** The first-run tour: a strip, not a modal wall (v2 02.5). */
@Component({
  selector: 'app-coach-marks',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective],
  templateUrl: './coach-marks.html',
  styleUrl: './coach-marks.scss',
})
export class CoachMarks {
  protected readonly coach = inject(CoachStore);
}
