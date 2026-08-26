import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  NodeProfileDetail,
  NodeProfileStore,
  SaveNodeProfile,
} from '../../core/directory/node-profile.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SettingsTabs } from './settings-tabs';

type SaveState = 'idle' | 'saving' | 'saved' | 'error';

/**
 * Authoring node profiles, and attaching them to units (v2 §10.6).
 *
 * Deliberately a plain editor over the stored shape, for the same reason the department settings screen is: what
 * this slice has to prove is that an administrator can invent vocabulary the platform has never heard of and
 * attach it mid-tree without a deployment. A friendlier taxonomy builder is worth having once §03 and §05 have
 * settled what item types and problem categories actually look like — designing one now would be guessing.
 *
 * The empty-means-inherit rule runs through the whole form. A blank field is sent as null, never as `{}`, because
 * the two mean opposite things: null keeps tracking the ancestor, `{}` freezes an empty override on top of it.
 */
@Component({
  selector: 'app-node-profiles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, PageHeader, SettingsTabs],
  templateUrl: './node-profiles.html',
  styleUrl: './node-profiles.scss',
})
export class NodeProfiles {
  private readonly profiles = inject(NodeProfileStore);
  private readonly directory = inject(DirectoryStore);

  protected readonly all = signal<readonly NodeProfileDetail[]>([]);
  protected readonly capabilityCodes = signal<readonly string[]>([]);
  protected readonly saveState = signal<SaveState>('idle');
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly selectedId = signal<string | null>(null);

  // The editor's fields. Strings throughout, including the array ones, because a comma-separated line is what an
  // administrator can actually type — and an empty line is how they say "inherit".
  protected readonly code = signal('');
  protected readonly labelKey = signal('');
  protected readonly activityTaxonomyJson = signal('');
  protected readonly boardArchetypes = signal('');
  protected readonly itemTypes = signal('');
  protected readonly capabilitiesJson = signal('');
  protected readonly solvesCategories = signal('');
  protected readonly budgetDefaultsJson = signal('');
  protected readonly headlinePattern = signal('');

  /** Attachment. Units rather than departments because that is the level v2 §10.4 says siblings diverge at. */
  protected readonly attachUnitId = signal('');
  protected readonly attachProfileId = signal('');

  protected readonly units = computed(() => this.directory.visibleUnits());

  protected readonly selected = computed(() =>
    this.all().find((profile) => profile.id === this.selectedId()) ?? null,
  );

  constructor() {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    try {
      const [profiles, capabilities] = await Promise.all([
        this.profiles.list(),
        this.profiles.capabilities(),
      ]);

      this.all.set(profiles);
      this.capabilityCodes.set(Object.keys(capabilities));
    } catch {
      this.errorMessage.set('profiles.loadFailed');
    }
  }

  protected edit(profile: NodeProfileDetail): void {
    this.selectedId.set(profile.id);
    this.code.set(profile.code);
    this.labelKey.set(profile.labelKey);
    this.activityTaxonomyJson.set(this.pretty(profile.activityTaxonomyJson));
    this.boardArchetypes.set(profile.boardArchetypes?.join(', ') ?? '');
    this.itemTypes.set(profile.itemTypes?.join(', ') ?? '');
    this.capabilitiesJson.set(this.pretty(profile.capabilitiesJson));
    this.solvesCategories.set(profile.solvesCategories?.join(', ') ?? '');
    this.budgetDefaultsJson.set(this.pretty(profile.budgetDefaultsJson));
    this.headlinePattern.set(profile.headlinePattern ?? '');
    this.saveState.set('idle');
    this.errorMessage.set(null);
  }

  /** Starts a blank profile. Every field empty means every field inherits until the author says otherwise. */
  protected startNew(): void {
    this.selectedId.set(null);
    this.code.set('');
    this.labelKey.set('');
    this.activityTaxonomyJson.set('');
    this.boardArchetypes.set('');
    this.itemTypes.set('');
    this.capabilitiesJson.set('');
    this.solvesCategories.set('');
    this.budgetDefaultsJson.set('');
    this.headlinePattern.set('');
    this.saveState.set('idle');
    this.errorMessage.set(null);
  }

  protected async save(): Promise<void> {
    this.saveState.set('saving');
    this.errorMessage.set(null);

    const payload: SaveNodeProfile = {
      code: this.code().trim(),
      labelKey: this.labelKey().trim(),
      activityTaxonomyJson: this.blank(this.activityTaxonomyJson()),
      boardArchetypes: this.list(this.boardArchetypes()),
      itemTypes: this.list(this.itemTypes()),
      capabilitiesJson: this.blank(this.capabilitiesJson()),
      solvesCategories: this.list(this.solvesCategories()),
      budgetDefaultsJson: this.blank(this.budgetDefaultsJson()),
      headlinePattern: this.blank(this.headlinePattern()),
    };

    try {
      const id = this.selectedId();
      const saved = id ? await this.profiles.update(id, payload) : await this.profiles.create(payload);

      this.selectedId.set(saved.id);
      this.saveState.set('saved');

      await this.reload();
    } catch (error) {
      this.saveState.set('error');
      this.errorMessage.set(this.describe(error));
    }
  }

  protected async clone(): Promise<void> {
    const source = this.selected();

    if (!source) {
      return;
    }

    this.saveState.set('saving');

    try {
      const copy = await this.profiles.clone(source.id, `${source.code}-COPY`, source.labelKey);

      await this.reload();
      this.edit(copy);
      this.saveState.set('saved');
    } catch (error) {
      this.saveState.set('error');
      this.errorMessage.set(this.describe(error));
    }
  }

  protected async attach(): Promise<void> {
    const unitId = this.attachUnitId();

    if (!unitId) {
      return;
    }

    this.saveState.set('saving');

    try {
      // Empty selection detaches. That is a real edit — the unit goes back to inheriting — so it is offered
      // rather than being something an administrator has to achieve by picking the parent's profile by hand.
      await this.profiles.attachToUnit(unitId, this.attachProfileId() || null);

      this.saveState.set('saved');

      // The viewer's own capabilities may have just changed, and the nav is rendered from them.
      this.directory.reload();
    } catch (error) {
      this.saveState.set('error');
      this.errorMessage.set(this.describe(error));
    }
  }

  private blank(value: string): string | null {
    return value.trim() === '' ? null : value.trim();
  }

  private list(value: string): readonly string[] | null {
    const entries = value
      .split(',')
      .map((entry) => entry.trim())
      .filter(Boolean);

    return entries.length === 0 ? null : entries;
  }

  private pretty(json: string | null): string {
    if (!json) {
      return '';
    }

    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }

  private describe(error: unknown): string {
    const problem = error as { error?: { detail?: string; title?: string } };

    return problem.error?.detail ?? problem.error?.title ?? 'profiles.saveFailed';
  }
}
