import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  MeetingLevel,
  MeetingOccurrenceView,
  MeetingsStore,
  MEETING_LEVELS,
} from '../../core/meetings/meetings.store';
import {
  ActionLinkType,
  ACTION_LINK_TYPES,
  MinutesStore,
  MinutesView,
  TrackerOwner,
  TrackerStatus,
} from '../../core/meetings/minutes.store';
import { DirectoryStore } from '../../core/directory/directory.store';
import { PersonSummary } from '../../core/directory/directory.models';
import { ProblemsStore } from '../../core/problems/problems.store';
import { CatalogStore } from '../../core/portfolio/catalog.store';
import { StrategyStore } from '../../core/strategy/strategy.store';
import { SessionStore } from '../../core/session/session.store';
import { CONTEXTUAL_ROLES } from '../../core/navigation/navigation';
import { PageHeader } from '../../shared/ui/page-header/page-header';

interface LinkOption {
  readonly id: string;
  readonly label: string;
}

/**
 * Meetings and their minutes (v2 §07.6).
 *
 * Three panels rather than three screens, because they are one loop: what is coming up, what came out of it, and
 * what somebody now owes. Splitting them would mean the person who has just written a CR has to go and find the
 * action they created to check it landed on the right person.
 */
@Component({
  selector: 'app-meetings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule, DatePipe, PageHeader],
  templateUrl: './meetings.html',
  styleUrl: './meetings.scss',
})
export class Meetings {
  protected readonly meetings = inject(MeetingsStore);
  protected readonly minutes = inject(MinutesStore);
  private readonly directory = inject(DirectoryStore);
  private readonly problems = inject(ProblemsStore);
  private readonly catalog = inject(CatalogStore);
  private readonly strategy = inject(StrategyStore);
  private readonly session = inject(SessionStore);

  protected readonly levels = MEETING_LEVELS;
  protected readonly linkTypes = ACTION_LINK_TYPES;

  protected readonly levelFilter = signal<MeetingLevel | null>(null);

  protected readonly editing = signal<MinutesView | null>(null);
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected readonly people = signal<readonly PersonSummary[]>([]);

  protected readonly draftAgenda = signal('');
  protected readonly draftSummary = signal('');

  protected readonly decisionText = signal('');
  protected readonly decisionRationale = signal('');
  protected readonly decisionBy = signal('');

  protected readonly actionTitle = signal('');
  protected readonly actionOwner = signal('');
  protected readonly actionDue = signal('');
  protected readonly actionLinkType = signal<ActionLinkType>('none');
  protected readonly actionLinkId = signal('');

  protected readonly isHead = computed(() =>
    this.session
      .roles()
      .some((role) => role === CONTEXTUAL_ROLES.nodeHead || role === CONTEXTUAL_ROLES.pmo),
  );

  /** Cancelled occurrences stay out: a CR for a meeting that did not happen is a record of nothing. */
  protected readonly occurrences = computed<readonly MeetingOccurrenceView[]>(() => {
    const level = this.levelFilter();

    return this.meetings
      .window()
      .filter((occurrence) => occurrence.status !== 'cancelled')
      .filter((occurrence) => level === null || occurrence.level === level);
  });

  protected readonly attendees = computed(() => new Set(this.editing()?.attendees ?? []));

  /** What an action may point at, resolved from the stores those things already live in (v2 §07.1). */
  protected readonly linkOptions = computed<readonly LinkOption[]>(() => {
    switch (this.actionLinkType()) {
      case 'problem':
        return this.problems.problems().map((problem) => ({
          id: problem.id,
          label: `${problem.code} · ${problem.title}`,
        }));
      case 'item':
        return this.catalog.cards().map((card) => ({ id: card.id, label: `${card.code} · ${card.name}` }));
      case 'objective':
        return this.strategy
          .objectives()
          .map((objective) => ({ id: objective.id, label: objective.title }));
      default:
        return [];
    }
  });

  protected readonly canPublish = computed(() => {
    const minutes = this.editing();

    if (!minutes || minutes.published) {
      return false;
    }

    return (
      this.draftSummary().trim().length > 0 ||
      minutes.decisions.length > 0 ||
      minutes.actions.length > 0
    );
  });

  protected setLevel(value: string): void {
    this.levelFilter.set((value || null) as MeetingLevel | null);
  }

  protected setTrackerOwner(value: string): void {
    this.minutes.trackerOwner.set(value as TrackerOwner);
  }

  protected setTrackerStatus(value: string): void {
    this.minutes.trackerStatus.set(value as TrackerStatus);
  }

  protected setLinkType(value: string): void {
    this.actionLinkType.set(value as ActionLinkType);
    this.actionLinkId.set('');
  }

  protected async write(occurrence: MeetingOccurrenceView): Promise<void> {
    this.failure.set(null);
    this.busy.set(true);

    try {
      const minutes = await this.minutes.open(occurrence.id);

      this.load(minutes);

      // Asked for the meeting's own scope rather than the whole directory: an attendance list of four hundred
      // names is a list nobody ticks.
      this.people.set(
        await this.directory.people(
          occurrence.scopeType === 'unit'
            ? { unitId: occurrence.scopeId ?? undefined }
            : { departmentId: occurrence.scopeId ?? undefined },
        ),
      );

      this.meetings.reloadWindow();
    } catch {
      this.failure.set('failed');
    } finally {
      this.busy.set(false);
    }
  }

  protected close(): void {
    this.editing.set(null);
  }

  protected toggleAttendance(personId: string): void {
    const minutes = this.editing();

    if (!minutes || minutes.published) {
      return;
    }

    const present = new Set(minutes.attendees);

    if (present.has(personId)) {
      present.delete(personId);
    } else {
      present.add(personId);
    }

    void this.save({
      attendees: [...present],
      absentees: this.people()
        .map((person) => person.id)
        .filter((id) => !present.has(id)),
    });
  }

  protected async saveText(): Promise<void> {
    await this.save({ agenda: this.draftAgenda(), summary: this.draftSummary() });
  }

  protected async decide(): Promise<void> {
    const minutes = this.editing();

    if (!minutes || this.decisionText().trim().length === 0) {
      return;
    }

    await this.run(async () => {
      this.load(
        await this.minutes.decide(minutes.id, {
          text: this.decisionText().trim(),
          rationale: blank(this.decisionRationale()),
          decidedBy: blank(this.decisionBy()),
        }),
      );

      this.decisionText.set('');
      this.decisionRationale.set('');
      this.decisionBy.set('');
    });
  }

  protected async assign(): Promise<void> {
    const minutes = this.editing();

    if (!minutes || this.actionTitle().trim().length === 0) {
      return;
    }

    await this.run(async () => {
      this.load(
        await this.minutes.assign(minutes.id, {
          title: this.actionTitle().trim(),
          ownerPersonId: blank(this.actionOwner()),
          due: blank(this.actionDue()),
          linkType: this.actionLinkType(),
          linkId: this.actionLinkType() === 'none' ? null : blank(this.actionLinkId()),
        }),
      );

      this.actionTitle.set('');
      this.actionDue.set('');
      this.actionLinkId.set('');
      this.actionLinkType.set('none');
    });
  }

  protected async settle(actionId: string): Promise<void> {
    const minutes = this.editing();

    if (!minutes) {
      return;
    }

    await this.run(async () => this.load(await this.minutes.settle(minutes.id, actionId, 'done')));
  }

  protected async settleFromTracker(minutesId: string, actionId: string): Promise<void> {
    await this.run(async () => {
      await this.minutes.settle(minutesId, actionId, 'done');
    });
  }

  protected async publish(): Promise<void> {
    const minutes = this.editing();

    if (!minutes) {
      return;
    }

    await this.run(async () => {
      // The text boxes are saved first: publishing a draft whose summary is still sitting unsent in an input is
      // the one way to distribute an empty CR that somebody had in fact written.
      await this.save({ agenda: this.draftAgenda(), summary: this.draftSummary() });

      this.load(await this.minutes.publish(minutes.id));
      this.meetings.reloadWindow();
    });
  }

  private async save(patch: {
    agenda?: string | null;
    attendees?: readonly string[];
    absentees?: readonly string[];
    summary?: string | null;
  }): Promise<void> {
    const minutes = this.editing();

    if (!minutes || minutes.published) {
      return;
    }

    await this.run(async () => this.load(await this.minutes.amend(minutes.id, patch)));
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.failure.set(null);
    this.busy.set(true);

    try {
      await work();
    } catch {
      this.failure.set('failed');
    } finally {
      this.busy.set(false);
    }
  }

  private load(minutes: MinutesView): void {
    this.editing.set(minutes);
    this.draftAgenda.set(minutes.agenda ?? '');
    this.draftSummary.set(minutes.summary ?? '');
  }
}

function blank(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}
