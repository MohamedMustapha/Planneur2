import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  OrgAdminStore,
  OrgLevelView,
  OrgNodeAdminView,
} from '../../core/admin/org-admin.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { PersonSummary } from '../../core/directory/directory.models';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SettingsTabs } from '../directory/settings-tabs';

/**
 * The org structure, and the trail of what was done to it (v2 §08.1, §08.3).
 *
 * The tree is rendered as one flat list indented by the depth the server computed. That is not a shortcut: the
 * number of levels is the deployment's, and a template that nested three loops would be the fixed ladder the
 * whole v2 org model exists to remove.
 *
 * Every control here is shown to everybody and refused by the server where it does not apply. A head sees the
 * whole tree — they can already read it — and gets a 403 the moment they try to move a branch that is not
 * beneath them. Hiding the button per row would mean re-deriving `access.can_write_org_node` in TypeScript, and
 * a second copy of a permission rule is a copy that drifts.
 */
@Component({
  selector: 'app-org-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, DatePipe, PageHeader, SettingsTabs],
  templateUrl: './org-admin.html',
  styleUrl: './org-admin.scss',
})
export class OrgAdmin {
  protected readonly org = inject(OrgAdminStore);
  private readonly directory = inject(DirectoryStore);

  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly people = signal<readonly PersonSummary[]>([]);

  protected readonly selected = signal<OrgNodeAdminView | null>(null);
  protected readonly draftName = signal('');
  protected readonly draftParent = signal('');
  protected readonly draftHead = signal('');

  protected readonly childCode = signal('');
  protected readonly childName = signal('');
  protected readonly childLevel = signal<number | null>(null);

  protected readonly editingLevel = signal<OrgLevelView | null>(null);

  /** A branch can move under anything that is not itself and not one of its own descendants. */
  protected readonly parentOptions = computed<readonly OrgNodeAdminView[]>(() => {
    const node = this.selected();

    if (!node) {
      return [];
    }

    const descendants = this.descendantsOf(node.id);

    return this.org.nodes().filter((candidate) => candidate.id !== node.id && !descendants.has(candidate.id));
  });

  protected async choose(node: OrgNodeAdminView): Promise<void> {
    this.failure.set(null);
    this.selected.set(node);
    this.draftName.set(node.name);
    this.draftParent.set(node.parentId ?? '');
    this.draftHead.set(node.headPersonId ?? '');
    this.childCode.set('');
    this.childName.set('');
    this.childLevel.set(this.nextLevelBelow(node.levelNo));
    this.org.auditNodeId.set(node.id);

    if (this.people().length === 0) {
      this.people.set(await this.directory.people());
    }
  }

  protected close(): void {
    this.selected.set(null);
    this.org.auditNodeId.set(null);
  }

  protected async rename(): Promise<void> {
    const node = this.selected();

    if (!node || this.draftName().trim().length === 0) {
      return;
    }

    await this.run(() => this.org.amend(node.id, { name: this.draftName().trim() }));
  }

  protected async reparent(): Promise<void> {
    const node = this.selected();

    if (!node) {
      return;
    }

    await this.run(() =>
      this.org.amend(node.id, { reparent: true, parentId: this.draftParent() || null }),
    );
  }

  protected async setHead(): Promise<void> {
    const node = this.selected();

    if (!node) {
      return;
    }

    await this.run(() => this.org.amend(node.id, { setHead: true, headPersonId: this.draftHead() || null }));
  }

  protected async toggleActive(): Promise<void> {
    const node = this.selected();

    if (!node) {
      return;
    }

    await this.run(async () => {
      await this.org.amend(node.id, { active: !node.active });

      this.selected.set(this.org.nodes().find((row) => row.id === node.id) ?? null);
    });
  }

  protected async addChild(): Promise<void> {
    const node = this.selected();
    const level = this.childLevel();

    if (!node || level === null || this.childCode().trim().length === 0) {
      return;
    }

    await this.run(async () => {
      await this.org.createNode({
        parentId: node.id,
        levelNo: level,
        code: this.childCode().trim(),
        name: this.childName().trim() || this.childCode().trim(),
      });

      this.childCode.set('');
      this.childName.set('');
    });
  }

  protected editLevel(level: OrgLevelView): void {
    this.editingLevel.set({ ...level });
  }

  protected patchLevel(patch: Partial<OrgLevelView>): void {
    const level = this.editingLevel();

    if (level) {
      this.editingLevel.set({ ...level, ...patch });
    }
  }

  protected async saveLevel(): Promise<void> {
    const level = this.editingLevel();

    if (!level) {
      return;
    }

    await this.run(async () => {
      await this.org.saveLevel(level);

      this.editingLevel.set(null);
    });
  }

  protected indent(depth: number): string {
    return `${depth * 1.25}rem`;
  }

  private nextLevelBelow(levelNo: number): number | null {
    const below = this.org
      .levels()
      .filter((level) => level.levelNo > levelNo)
      .sort((left, right) => left.levelNo - right.levelNo);

    return below[0]?.levelNo ?? null;
  }

  private descendantsOf(nodeId: string): ReadonlySet<string> {
    const found = new Set<string>();
    const nodes = this.org.nodes();
    let frontier = [nodeId];

    while (frontier.length > 0) {
      const children = nodes.filter((node) => node.parentId !== null && frontier.includes(node.parentId));

      frontier = children.filter((child) => !found.has(child.id)).map((child) => child.id);
      frontier.forEach((id) => found.add(id));
    }

    return found;
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.failure.set(null);
    this.busy.set(true);

    try {
      await work();
    } catch (error) {
      const refusal = error as { status?: number };

      this.failure.set(refusal.status === 403 ? 'forbidden' : 'failed');
    } finally {
      this.busy.set(false);
    }
  }
}
