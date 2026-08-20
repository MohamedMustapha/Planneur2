import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  ConnectionView,
  EXTERNAL_PROVIDERS,
  ExternalProvider,
  ExternalWorkItemView,
  IntegrationsStore,
  MAPPING_KINDS,
  MappingKind,
  targetsProject,
} from '../../core/integrations/integrations.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { ProjectsStore } from '../../core/projects/projects.store';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SettingsTabs } from '../directory/settings-tabs';

/**
 * Connections to Azure DevOps and ServiceNow, and what they mirror.
 *
 * The screen is deliberately blunt about the direction of the arrow. Every control here configures a *pull*;
 * there is no button that sends anything outward, because the API has none. The preview under a connection is
 * what makes that legible to whoever is configuring it: they press "sync", and what appears is a list of the
 * other system's work, read-only, with a link back to the record it came from.
 *
 * The secret is a name, not a value. The field asks for an `auth_ref` and says so — the token itself lives in the
 * deployment's vault, which is why a department head can safely be the person who fills this form in.
 */
@Component({
  selector: 'app-integrations-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, DatePipe, PageHeader, SettingsTabs],
  templateUrl: './integrations-admin.html',
  styleUrl: './integrations-admin.scss',
})
export class IntegrationsAdmin {
  private readonly directory = inject(DirectoryStore);
  private readonly projects = inject(ProjectsStore);

  protected readonly integrations = inject(IntegrationsStore);
  protected readonly departments = inject(DepartmentScopeStore);

  protected readonly providers = EXTERNAL_PROVIDERS;
  protected readonly mappingKinds = MAPPING_KINDS;

  protected readonly units = computed(() => this.directory.units());
  protected readonly projectList = computed(() => this.projects.projects());

  protected readonly errorMessage = signal<string | null>(null);
  protected readonly isBusy = signal(false);

  /** Which connection's mirror preview is open, and what it holds. One at a time: this is a diagnostic, not a feed. */
  protected readonly previewFor = signal<string | null>(null);
  protected readonly preview = signal<readonly ExternalWorkItemView[]>([]);

  // --- The new-connection form -----------------------------------------------------------------------------------
  protected readonly formProvider = signal<ExternalProvider>('azure-devops');
  protected readonly formName = signal('');
  protected readonly formBaseUrl = signal('');
  protected readonly formAuthRef = signal('');
  protected readonly formProjectOrQueue = signal('');
  protected readonly formCurrentSprint = signal('');
  protected readonly formPollMinutes = signal(15);

  // --- The new-mapping form, per connection ----------------------------------------------------------------------
  protected readonly mappingFor = signal<string | null>(null);
  protected readonly mappingKind = signal<MappingKind>('area-path');
  protected readonly mappingValue = signal('');
  protected readonly mappingTarget = signal('');

  protected readonly mappingTargetsProject = computed(() => targetsProject(this.mappingKind()));

  /** What the source calls the thing being pulled. DevOps has team projects; ServiceNow has assignment groups. */
  protected readonly projectOrQueueLabel = computed(() =>
    this.formProvider() === 'servicenow' ? 'integrations.queue' : 'integrations.teamProject',
  );

  protected async submit(): Promise<void> {
    const departmentId = this.departments.selected()?.id;

    if (!departmentId || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    this.errorMessage.set(null);

    try {
      await this.integrations.create({
        departmentId,
        provider: this.formProvider(),
        name: this.formName(),
        baseUrl: this.formBaseUrl(),
        authRef: this.formAuthRef(),
        projectOrQueue: this.formProjectOrQueue(),
        currentSprint: this.formCurrentSprint() || null,
        pollInterval: this.asInterval(this.formPollMinutes()),
        active: true,
      });

      this.formName.set('');
      this.formBaseUrl.set('');
      this.formAuthRef.set('');
      this.formProjectOrQueue.set('');
      this.formCurrentSprint.set('');
    } catch (error) {
      this.errorMessage.set(this.describe(error));
    } finally {
      this.isBusy.set(false);
    }
  }

  protected async toggleActive(connection: ConnectionView): Promise<void> {
    await this.guard(() =>
      this.integrations.update(connection.id, {
        departmentId: connection.departmentId,
        provider: connection.provider,
        name: connection.name,
        baseUrl: connection.baseUrl,
        authRef: connection.authRef,
        projectOrQueue: connection.projectOrQueue,
        currentSprint: connection.currentSprint,
        pollInterval: connection.pollInterval,
        active: !connection.active,
      }),
    );
  }

  protected async syncNow(connection: ConnectionView): Promise<void> {
    await this.guard(async () => {
      await this.integrations.sync(connection.id);

      // The server answered 202 and the pull runs behind it, so there is nothing to await. Reloading the list is
      // how the outcome arrives — on the connection's own last-sync fields, where it also lives for the next
      // person to look.
      this.integrations.reload();
    });
  }

  protected async remove(connection: ConnectionView): Promise<void> {
    await this.guard(() => this.integrations.remove(connection.id));
  }

  protected async addMapping(connection: ConnectionView): Promise<void> {
    const target = this.mappingTarget();

    if (!target) {
      return;
    }

    await this.guard(async () => {
      await this.integrations.addMapping(connection.id, {
        kind: this.mappingKind(),
        externalValue: this.mappingValue(),
        projectId: this.mappingTargetsProject() ? target : null,
        unitId: this.mappingTargetsProject() ? null : target,
      });

      this.mappingValue.set('');
      this.mappingTarget.set('');
    });
  }

  protected async removeMapping(connection: ConnectionView, mappingId: string): Promise<void> {
    await this.guard(() => this.integrations.removeMapping(connection.id, mappingId));
  }

  protected async togglePreview(connection: ConnectionView): Promise<void> {
    if (this.previewFor() === connection.id) {
      this.previewFor.set(null);
      this.preview.set([]);
      return;
    }

    this.previewFor.set(connection.id);
    this.preview.set(await this.integrations.workItems(connection.id));
  }

  protected openMappingForm(connectionId: string): void {
    this.mappingFor.set(this.mappingFor() === connectionId ? null : connectionId);
  }

  protected setMappingKind(kind: MappingKind): void {
    this.mappingKind.set(kind);

    // The target list changes shape with the kind — projects for an area path, units for an assignment group — so
    // a selection made under the old kind would be a valid id pointing at the wrong table.
    this.mappingTarget.set('');
  }

  protected projectCode(projectId: string | null): string {
    return this.projectList().find((project) => project.id === projectId)?.code ?? '—';
  }

  protected unitName(unitId: string | null): string {
    return this.units().find((unit) => unit.id === unitId)?.name ?? '—';
  }

  /** `hh:mm:ss` reads as zero for an on-demand connection, which the template shows as such. */
  protected isOnDemand(pollInterval: string): boolean {
    return pollInterval.startsWith('00:00:00');
  }

  private asInterval(minutes: number): string {
    const clamped = Math.max(0, Math.min(minutes, 24 * 60));

    return `${String(Math.floor(clamped / 60)).padStart(2, '0')}:${String(clamped % 60).padStart(2, '0')}:00`;
  }

  private async guard(action: () => Promise<void>): Promise<void> {
    if (this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    this.errorMessage.set(null);

    try {
      await action();
    } catch (error) {
      this.errorMessage.set(this.describe(error));
    } finally {
      this.isBusy.set(false);
    }
  }

  /**
   * The server's own words where it gave any.
   *
   * Every refusal this screen can provoke is explanatory — an unlisted host, a poll interval nobody meant, a
   * mapping pointing at the wrong kind of thing — and replacing them with a generic message would throw away the
   * only part of the response that helps.
   */
  private describe(error: unknown): string {
    const detail = (error as { error?: { detail?: string; title?: string } })?.error;

    return detail?.detail ?? detail?.title ?? 'integrations.saveFailed';
  }
}
