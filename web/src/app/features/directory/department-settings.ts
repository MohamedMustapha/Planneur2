import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { FormsModule } from '@angular/forms';
import { DepartmentConfig } from '../../core/directory/directory.models';
import { DirectoryStore } from '../../core/directory/directory.store';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';

type SaveState = 'idle' | 'saving' | 'saved' | 'error';

/**
 * The per-department knobs: activity taxonomy, role labels, kudo rules, iteration presets, weekly target.
 *
 * This screen is the whole mechanism behind "department-agnostic". HR or Finance adopt the platform by editing
 * these values, not by anyone adding a branch in code — so the editor is deliberately plain and shows the JSON it
 * is going to send. A friendlier editor per section is worth building once S5, S6 and S9 have settled what each
 * shape actually needs; inventing one now would be guessing at three specs that do not exist yet.
 */
@Component({
  selector: 'app-department-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, PageHeader],
  templateUrl: './department-settings.html',
  styleUrl: './department-settings.scss',
})
export class DepartmentSettings {
  private readonly directory = inject(DirectoryStore);
  protected readonly departments = inject(DepartmentScopeStore);

  protected readonly config = signal<DepartmentConfig | null>(null);
  protected readonly saveState = signal<SaveState>('idle');
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly departmentId = computed(() => this.departments.selected()?.id ?? null);

  // Edited copies. Kept separate from the loaded config so Cancel is a reload rather than an undo stack.
  protected readonly activityTaxonomyJson = signal('{}');
  protected readonly roleLabelsJson = signal('{}');
  protected readonly kudoRulesJson = signal('{}');
  protected readonly iterationPresetsJson = signal('[]');
  // Both of these are edited here and read nowhere else on this screen, but they must still make the round trip:
  // the update request defaults anything it is not sent to "{}", so a save that left them out would quietly erase
  // a department's shift slots and working day.
  protected readonly shiftTemplatesJson = signal('{}');
  protected readonly workingDayJson = signal('{}');
  protected readonly defaultBoardLayout = signal('week');
  protected readonly weeklyTargetHours = signal(35);
  protected readonly enforceWeeklyTarget = signal(false);

  constructor() {
    effect(() => {
      const id = this.departmentId();

      if (id) {
        void this.load(id);
      }
    });
  }

  protected async load(departmentId: string): Promise<void> {
    try {
      const config = await this.directory.config(departmentId);

      this.config.set(config);
      this.activityTaxonomyJson.set(this.pretty(config.activityTaxonomyJson));
      this.roleLabelsJson.set(this.pretty(config.roleLabelsJson));
      this.kudoRulesJson.set(this.pretty(config.kudoRulesJson));
      this.iterationPresetsJson.set(this.pretty(config.iterationPresetsJson));
      this.shiftTemplatesJson.set(this.pretty(config.shiftTemplatesJson));
      this.workingDayJson.set(this.pretty(config.workingDayJson));
      this.defaultBoardLayout.set(config.defaultBoardLayout);
      this.weeklyTargetHours.set(config.weeklyTargetHours);
      this.enforceWeeklyTarget.set(config.enforceWeeklyTarget);
      this.errorMessage.set(null);
    } catch {
      this.config.set(null);
      this.errorMessage.set('settings.loadFailed');
    }
  }

  protected async save(): Promise<void> {
    const departmentId = this.departmentId();

    if (!departmentId) {
      return;
    }

    this.saveState.set('saving');

    try {
      const saved = await this.directory.saveConfig(departmentId, {
        activityTaxonomyJson: this.activityTaxonomyJson(),
        roleLabelsJson: this.roleLabelsJson(),
        kudoRulesJson: this.kudoRulesJson(),
        iterationPresetsJson: this.iterationPresetsJson(),
        shiftTemplatesJson: this.shiftTemplatesJson(),
        workingDayJson: this.workingDayJson(),
        defaultBoardLayout: this.defaultBoardLayout(),
        weeklyTargetHours: this.weeklyTargetHours(),
        enforceWeeklyTarget: this.enforceWeeklyTarget(),
      });

      this.config.set(saved);
      this.saveState.set('saved');
      this.errorMessage.set(null);
    } catch (error) {
      this.saveState.set('error');

      // The server validates structure and rejects with a ProblemDetails title. Showing it beats a generic
      // "save failed" — the person editing needs to know *which* field is malformed.
      const detail = (error as { error?: { title?: string; detail?: string } }).error;

      this.errorMessage.set(detail?.detail ?? detail?.title ?? 'settings.saveFailed');
    }
  }

  protected cancel(): void {
    const id = this.departmentId();

    if (id) {
      void this.load(id);
    }

    this.saveState.set('idle');
  }

  /** Reformats stored JSON for editing. Invalid JSON is shown as-is so it can be repaired rather than lost. */
  private pretty(json: string): string {
    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }
}
