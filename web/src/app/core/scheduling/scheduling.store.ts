import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { startOfWeek } from '../time/week';

/** Mirrors `Cracra.Modules.Scheduling.Contracts`. */

export type BoardType = 'my' | 'team' | 'unit' | 'project' | 'department';

export type BoardArchetype = 'work-orders' | 'shifts' | 'task-progress';

export interface BoardResource {
  readonly id: string;
  readonly name: string;
  /** person | project-line | unit | department | lane. Drives which row template renders. */
  readonly kind: string;
  readonly parentId: string | null;
  readonly color: string | null;
  readonly subtitleKey: string | null;
}

export interface BoardEvent {
  readonly id: string;
  readonly resourceId: string;
  readonly title: string;
  readonly start: string;
  readonly end: string;
  readonly kind: string;
  readonly color: string | null;
  readonly cssClass: string | null;
  readonly progress: number | null;
  readonly editable: boolean;
  readonly activityTypeCode: string | null;
  readonly projectId: string | null;
  readonly externalRef: string | null;
}

export interface BoardOverlay {
  readonly id: string;
  readonly kind: string;
  readonly title: string;
  readonly from: string;
  readonly to: string;
  readonly color: string | null;
}

export interface WorkOrderView {
  readonly id: string;
  readonly reference: string;
  readonly title: string;
  readonly description: string | null;
  readonly source: string;
  readonly externalRef: string | null;
  readonly projectId: string | null;
  readonly projectCode: string | null;
  readonly activityTypeCode: string;
  readonly unitId: string;
  readonly assignedToPersonId: string | null;
  readonly assignedToName: string | null;
  readonly scheduledStart: string | null;
  readonly scheduledEnd: string | null;
  readonly estimatedHours: number;
  readonly state: string;
}

export interface CoverageWarning {
  readonly day: string;
  readonly slotCode: string;
  readonly required: number;
  readonly scheduled: number;
}

export interface BoardPayload {
  readonly boardType: BoardType;
  readonly archetype: BoardArchetype;
  readonly title: string;
  readonly from: string;
  readonly to: string;
  readonly resources: readonly BoardResource[];
  readonly events: readonly BoardEvent[];
  readonly overlays: readonly BoardOverlay[];
  readonly pool: readonly WorkOrderView[];
  readonly coverage: readonly CoverageWarning[];
  readonly canAssign: boolean;
}

export interface ShiftTemplate {
  readonly code: string;
  readonly labelKey: string;
  readonly start: string;
  readonly end: string;
  readonly minimumStaff: number;
  readonly color: string | null;
}

/**
 * The boards, as signals.
 *
 * One store for all five, because they are one endpoint: which board is showing is a parameter, not a different
 * screen. Keeping them apart would mean five stores each re-deriving the same week arithmetic.
 */
@Injectable({ providedIn: 'root' })
export class SchedulingStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  readonly boardType = signal<BoardType>('my');
  readonly scopeId = signal<string | null>(null);
  readonly weekOffset = signal(0);

  readonly week = computed(() => {
    const monday = startOfWeek(new Date());
    monday.setDate(monday.getDate() + this.weekOffset() * 7);

    const sunday = new Date(monday);
    sunday.setDate(sunday.getDate() + 6);

    return { monday, sunday };
  });

  private readonly boardResource = httpResource<BoardPayload>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    // The project board is the one board that cannot be derived from who the caller is. Asking anyway and
    // rendering the server's refusal would be a worse answer than the client saying what it already knows: no
    // project has been chosen yet.
    if (this.boardType() === 'project' && !this.scopeId()) {
      return undefined;
    }

    const { monday, sunday } = this.week();
    const params = new URLSearchParams({
      type: this.boardType(),
      from: asDate(monday),
      to: asDate(sunday),
    });

    const scope = this.scopeId();

    if (scope) {
      params.set('scopeId', scope);
    }

    return `/api/scheduling/board?${params}`;
  });

  readonly board = computed(() => this.boardResource.value());
  readonly isLoading = this.boardResource.isLoading;

  // A board that failed to load is not the same as an empty one, and the difference matters: the first needs a
  // message, the second is just a quiet week.
  readonly error = computed(() => this.boardResource.error());

  readonly archetype = computed<BoardArchetype>(() => this.board()?.archetype ?? 'task-progress');
  readonly resources = computed<readonly BoardResource[]>(() => this.board()?.resources ?? []);
  readonly events = computed<readonly BoardEvent[]>(() => this.board()?.events ?? []);
  readonly overlays = computed<readonly BoardOverlay[]>(() => this.board()?.overlays ?? []);
  readonly pool = computed<readonly WorkOrderView[]>(() => this.board()?.pool ?? []);
  readonly coverage = computed<readonly CoverageWarning[]>(() => this.board()?.coverage ?? []);
  readonly canAssign = computed(() => this.board()?.canAssign ?? false);

  show(type: BoardType, scopeId: string | null = null): void {
    this.boardType.set(type);
    this.scopeId.set(scopeId);
  }

  stepWeek(by: number): void {
    this.weekOffset.update((offset) => offset + by);
  }

  async assign(workOrderId: string, personId: string, start: Date): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/scheduling/work-orders/${workOrderId}/assign`, {
        personId,
        start: start.toISOString(),
      }),
    );

    this.reload();
  }

  async unassign(workOrderId: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/scheduling/work-orders/${workOrderId}/unassign`, null));
    this.reload();
  }

  async refreshPool(unitId: string): Promise<number> {
    const result = await firstValueFrom(
      this.http.post<{ created: number }>('/api/scheduling/work-orders/pool/refresh', { unitId }),
    );

    this.reload();

    return result.created;
  }

  async shiftTemplates(): Promise<readonly ShiftTemplate[]> {
    return firstValueFrom(this.http.get<ShiftTemplate[]>('/api/scheduling/shifts/templates'));
  }

  async planShift(personId: string, templateCode: string, day: Date): Promise<void> {
    await firstValueFrom(
      this.http.post('/api/scheduling/shifts', { personId, templateCode, day: asDate(day) }),
    );

    this.reload();
  }

  async deleteShift(shiftId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/scheduling/shifts/${shiftId}`));
    this.reload();
  }

  /** 6c: a planned block dragged to a new time. The server refuses anything that is not planned. */
  async rescheduleTask(entryId: string, start: Date, end: Date): Promise<void> {
    await firstValueFrom(
      this.http.put(`/api/scheduling/tasks/${entryId}/schedule`, {
        start: start.toISOString(),
        end: end.toISOString(),
      }),
    );

    this.reload();
  }

  reload(): void {
    this.boardResource.reload();
  }
}

function asDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}
