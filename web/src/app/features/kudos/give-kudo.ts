import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { EligiblePeer, KudoRulesView, KudosStore } from '../../core/kudos/kudos.store';

/**
 * The give-kudo modal.
 *
 * Its own component rather than a block inside the wall, because the design puts it on a teammate's card in the
 * side rail of the personal board as well — and the two would drift the moment one of them learned about the cap
 * and the other did not.
 *
 * The rules are re-fetched for the person chosen, not read from the caller's own department. Across a shared
 * project the recipient may sit in a department that prices recognition differently, and showing them their own
 * department's categories would offer a list the server then refuses.
 */
@Component({
  selector: 'app-give-kudo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, FormsModule],
  templateUrl: './give-kudo.html',
  styleUrl: './give-kudo.scss',
})
export class GiveKudo {
  private readonly kudos = inject(KudosStore);

  /** Pre-selected recipient, when the modal was opened from somebody's card. */
  readonly person = input<string | null>(null);

  readonly closed = output<void>();
  readonly given = output<void>();

  protected readonly toPersonId = signal('');
  protected readonly category = signal('');
  protected readonly message = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The recipient's department's rules. Null until somebody is chosen — there is nothing to price yet. */
  protected readonly rules = signal<KudoRulesView | null>(null);

  protected readonly peers = computed(() => this.kudos.peers());

  /** What the giver has left this month, from their own department's cap. */
  protected readonly remaining = computed(
    () => this.rules()?.remainingThisMonth ?? this.kudos.remaining(),
  );

  protected readonly capReached = computed(() => this.remaining() <= 0);

  protected readonly canGive = computed(
    () =>
      this.toPersonId().length > 0 &&
      this.category().length > 0 &&
      this.message().trim().length > 0 &&
      !this.capReached(),
  );

  constructor() {
    const preselected = this.person();

    if (preselected) {
      void this.choose(preselected);
    }
  }

  protected async choose(personId: string): Promise<void> {
    this.toPersonId.set(personId);
    this.category.set('');
    this.error.set(null);

    if (!personId) {
      this.rules.set(null);
      return;
    }

    const rules = await this.kudos.rulesFor(personId);

    this.rules.set(rules);

    // One category is not a choice, and five is a menu somebody has to read. Pre-selecting nothing would be
    // honest but slower; pre-selecting the cheapest is the least presumptuous default that still saves a click.
    this.category.set(rules.categories[0]?.code ?? '');
  }

  protected relationLabel(peer: EligiblePeer): string {
    return `kudos.relation.${peer.relation}`;
  }

  protected async give(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await this.kudos.give(this.toPersonId(), this.category(), this.message().trim());

      this.given.emit();
      this.closed.emit();
    } catch (failure) {
      const problem = failure as { error?: { detail?: string; title?: string } };

      this.error.set(problem.error?.detail ?? problem.error?.title ?? 'kudos.giveFailed');
    } finally {
      this.busy.set(false);
    }
  }

  protected close(): void {
    this.closed.emit();
  }
}
