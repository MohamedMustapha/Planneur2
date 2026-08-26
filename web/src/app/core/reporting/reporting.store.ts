import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../session/session.store';
import { ANTI_FORGERY_HEADER } from '../session/csrf.interceptor';

/** Mirrors `Cracra.Modules.Reporting.Contracts`. */

export type ReportScope = 'me' | 'node' | 'item' | 'portfolio';

export type ReportPeriodKind = 'week' | 'month' | 'custom';

export interface ReportPeriodView {
  readonly kind: ReportPeriodKind;
  readonly from: string;
  readonly to: string;
  readonly isoYear: number | null;
  readonly isoWeek: number | null;
  readonly labelKey: string;
}

export interface ReportMetric {
  readonly key: string;
  readonly value: number;
  /** hours | count | percent | currency — decides the formatting, which is the client's job. */
  readonly unit: string;
}

export interface ReportRow {
  readonly key: string;
  /** A translation key or a literal; render whichever resolves. */
  readonly label: string;
  readonly values: readonly number[];
}

export interface ReportTable {
  readonly titleKey: string;
  readonly columnKeys: readonly string[];
  readonly rows: readonly ReportRow[];
  readonly identifiesPeople: boolean;
}

export interface ReportNote {
  readonly key: string;
  readonly text: string | null;
  readonly severity: string | null;
}

export interface ReportSection {
  readonly key: string;
  readonly titleKey: string;
  readonly metrics: readonly ReportMetric[];
  readonly tables: readonly ReportTable[];
  readonly notes: readonly ReportNote[];
}

// --- The brief (v2 §07.3) ---------------------------------------------------------------------------------------
//
// A different rendering of the same period, not a different report. The table answers "give me every figure"; the
// brief answers "what do I say about this at the COPIL", which is one sentence per branch and no decimals.

export interface BriefTotals {
  readonly actualHours: number;
  readonly plannedHours: number;
  readonly entryCount: number;
  readonly peopleCount: number;
}

export interface BriefHighlight {
  readonly activityTypeCode: string;
  readonly actualHours: number;
}

export interface BriefUpcoming {
  readonly kind: string;
  readonly nameKey: string;
  readonly at: string;
  readonly severity: string | null;
}

export interface NodeBriefBlock {
  readonly nodeId: string;
  readonly parentId: string | null;
  readonly levelNo: number;
  readonly code: string;
  readonly name: string;
  /** What people attached directly to this node did. */
  readonly own: BriefTotals;
  /** `own` plus every descendant's — the number a head reads on one line per child. */
  readonly subtree: BriefTotals;
  readonly children: readonly NodeBriefBlock[];
  readonly highlights: readonly BriefHighlight[];
}

export interface NodeBriefView {
  readonly nodeId: string;
  readonly period: ReportPeriodView;
  readonly depth: string;
  readonly node: NodeBriefBlock;
  readonly upcoming: readonly BriefUpcoming[];
}

export interface ReportSummaryView {
  readonly id: string;
  readonly text: string;
  readonly model: string;
  readonly language: string;
  readonly createdAt: string;
  /** True when the figures moved since the text was written. The panel offers a regenerate. */
  readonly stale: boolean;
}

export interface ReportView {
  readonly id: string;
  readonly scope: ReportScope;
  readonly scopeId: string | null;
  readonly scopeLabel: string;
  readonly period: ReportPeriodView;
  readonly language: string;
  readonly sections: readonly ReportSection[];
  readonly availableScopes: readonly ReportScope[];
  readonly summary: ReportSummaryView | null;
  readonly generatedAt: string;
}

export interface ReportExportView {
  readonly reportId: string;
  readonly format: string;
  readonly url: string;
  readonly expiresAt: string;
  readonly bytes: number;
}

/**
 * The contextual report, as signals.
 *
 * The deterministic half is an `httpResource`, so tables and figures render as soon as they arrive. The narrative
 * is not: it is slow, optional and clearly machine-written, and binding it to the same resource would hold the
 * numbers back behind a model that takes seconds to answer.
 */
@Injectable({ providedIn: 'root' })
export class ReportingStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  /**
   * Null means "whatever my role grants".
   *
   * Deliberately not defaulted client-side. Which scope is widest for this viewer is a server decision, and
   * guessing it here would put the visibility matrix in two places.
   */
  readonly scope = signal<ReportScope | null>(null);
  readonly scopeId = signal<string | null>(null);
  readonly period = signal<ReportPeriodKind>('week');

  /** Week offset from the current one, as on the boards. Negative is the past. */
  readonly offset = signal(0);

  private readonly reportResource = httpResource<ReportView>(() => {
    if (!this.session.isAuthenticated()) {
      return undefined;
    }

    const scope = this.scope();

    // The project report is the one that cannot be derived from who the caller is. Asking anyway and rendering
    // the server's refusal is a worse answer than the client saying what it already knows.
    if (scope === 'item' && !this.scopeId()) {
      return undefined;
    }

    const params = new URLSearchParams({
      period: this.period(),
      from: asDate(this.anchor()),
    });

    if (scope) {
      params.set('scope', scope);
    }

    const id = this.scopeId();

    if (id) {
      params.set('scopeId', id);
    }

    return `/api/reports?${params}`;
  });

  /**
   * Brief or details.
   *
   * The brief is the default, which is the whole point of §07.3: the raw per-unit table with its decimals is a
   * consultation view, and handing somebody that when they asked "how did the week go" is why the synthèse was
   * not being used. The table stays one click away rather than being removed.
   */
  readonly rendering = signal<'brief' | 'details'>('brief');

  /** All expands the whole subtree; 1 is one line per child, which is what a COPIL reads. */
  readonly briefDepth = signal<'1' | 'all'>('1');

  private readonly briefResource = httpResource<NodeBriefView>(() => {
    if (!this.session.isAuthenticated() || this.rendering() !== 'brief') {
      return undefined;
    }

    const params = new URLSearchParams({
      period: this.period(),
      from: asDate(this.anchor()),
      depth: this.briefDepth(),
    });

    // No nodeId: the server resolves the caller's own branch, which is the only answer the client could give
    // anyway and one it would have to ask for first.
    return `/api/reports/brief?${params}`;
  });

  readonly brief = computed<NodeBriefView | undefined>(() => this.briefResource.value());
  readonly briefLoading = this.briefResource.isLoading;
  readonly briefError = computed(() => this.briefResource.error());

  /** The anchor date the period is resolved around — a day inside the week or month being reported on. */
  readonly anchor = computed(() => {
    const date = new Date();

    if (this.period() === 'month') {
      date.setMonth(date.getMonth() + this.offset());
    } else {
      date.setDate(date.getDate() + this.offset() * 7);
    }

    return date;
  });

  readonly report = computed(() => this.reportResource.value());
  readonly isLoading = this.reportResource.isLoading;
  readonly error = computed(() => this.reportResource.error());

  readonly sections = computed<readonly ReportSection[]>(() => this.report()?.sections ?? []);
  readonly availableScopes = computed<readonly ReportScope[]>(
    () => this.report()?.availableScopes ?? [],
  );

  // --- The narrative --------------------------------------------------------------------------------------

  /** What has streamed in so far. Cleared when a new generation starts. */
  readonly streaming = signal<string | null>(null);
  readonly generating = signal(false);

  /** The stored narrative, or whatever is currently streaming over the top of it. */
  readonly summaryText = computed(() => this.streaming() ?? this.report()?.summary?.text ?? null);

  readonly summary = computed(() => this.report()?.summary ?? null);

  show(scope: ReportScope | null, scopeId: string | null = null): void {
    this.scope.set(scope);
    this.scopeId.set(scopeId);
    this.streaming.set(null);
  }

  step(by: number): void {
    this.offset.update((offset) => offset + by);
    this.streaming.set(null);
  }

  setPeriod(period: ReportPeriodKind): void {
    this.period.set(period);
    this.offset.set(0);
    this.streaming.set(null);
  }

  /**
   * Streams the narrative in, then persists the finished text.
   *
   * Two calls on purpose. The stream is for the wait — a blank panel for eight seconds reads as broken — and it
   * deliberately stores nothing, so a reader who navigates away mid-sentence does not leave half a summary
   * cached. The POST afterwards is what writes the complete text.
   */
  async generate(force = false): Promise<void> {
    this.generating.set(true);
    this.streaming.set('');

    const body = this.summaryBody(force);

    try {
      const response = await fetch('/api/reports/summary/stream', {
        method: 'POST',
        // fetch rather than HttpClient, because the streaming body is the point and HttpClient buffers it —
        // which means the CSRF header the interceptor would have added has to be set here by hand.
        headers: { 'Content-Type': 'application/json', [ANTI_FORGERY_HEADER]: '1' },
        body: JSON.stringify(body),
      });

      if (!response.ok || !response.body) {
        throw new Error(String(response.status));
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      for (;;) {
        const { done, value } = await reader.read();

        if (done) {
          break;
        }

        buffer += decoder.decode(value, { stream: true });

        // Frames are separated by a blank line; anything after the last one is a partial frame to keep.
        const frames = buffer.split('\n\n');
        buffer = frames.pop() ?? '';

        for (const frame of frames) {
          const data = frame.replace(/^data:\s?/, '');

          if (data === '[DONE]' || data.length === 0) {
            continue;
          }

          this.streaming.update((text) => (text ?? '') + data.replaceAll('\\n', '\n'));
        }
      }

      // Persisted only once the stream finished, which is also what makes it appear on the next load.
      await firstValueFrom(this.http.post<ReportSummaryView>('/api/reports/summary', body));

      this.streaming.set(null);
      this.reportResource.reload();
    } finally {
      this.generating.set(false);
    }
  }

  async export(format = 'pdf'): Promise<ReportExportView> {
    const id = this.report()?.id;

    if (!id) {
      throw new Error('No report to export.');
    }

    return firstValueFrom(
      this.http.get<ReportExportView>(`/api/reports/${id}/export?format=${format}`),
    );
  }

  reload(): void {
    this.reportResource.reload();
  }

  private summaryBody(force: boolean) {
    const report = this.report();

    return {
      // The report's own resolved scope and window, not the store's — so the narrative describes exactly the
      // figures on screen even when the store is still holding "whatever my role grants".
      scope: report?.scope ?? this.scope(),
      scopeId: report?.scopeId ?? this.scopeId(),
      period: 'custom',
      from: report?.period.from,
      to: report?.period.to,
      lang: report?.language,
      force,
    };
  }
}

function asDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}
