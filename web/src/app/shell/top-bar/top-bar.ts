import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { UpperCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { DepartmentScope, DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { CoachStore } from '../../core/coach/coach.store';
import { FocusStore } from '../../core/focus/focus.store';
import { ObligationsStore } from '../../core/obligations/obligations.store';
import { Language, LanguageStore } from '../../core/i18n/language.store';
import { LayoutStore } from '../../core/layout/layout.store';
import { SessionStore } from '../../core/session/session.store';
import { PreferencesStore } from '../../core/preferences/preferences.store';
import { Theme, ThemeStore } from '../../core/theme/theme.store';
import { formatDayMonth, workWeek } from '../../core/time/week';

@Component({
  selector: 'app-top-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, UpperCasePipe, FormsModule],
  templateUrl: './top-bar.html',
  styleUrl: './top-bar.scss',
})
export class TopBar {
  protected readonly layout = inject(LayoutStore);
  protected readonly focus = inject(FocusStore);
  private readonly coach = inject(CoachStore);
  protected readonly obligations = inject(ObligationsStore);
  protected readonly session = inject(SessionStore);
  protected readonly theme = inject(ThemeStore);
  protected readonly language = inject(LanguageStore);
  protected readonly departments = inject(DepartmentScopeStore);
  protected readonly preferences = inject(PreferencesStore);

  protected readonly departmentMenuOpen = signal(false);
  protected readonly userMenuOpen = signal(false);

  protected readonly week = computed(() => workWeek(this.layout.weekOffset()));

  protected readonly weekLabel = computed(() => {
    const week = this.week();

    return `${week.isoWeek} · ${formatDayMonth(week.monday)} → ${formatDayMonth(week.friday)}`;
  });

  protected toggleDepartmentMenu(): void {
    this.departmentMenuOpen.update((open) => !open);
    this.userMenuOpen.set(false);
  }

  protected replayCoach(): void {
    this.coach.replay();
    this.userMenuOpen.set(false);
  }

  protected toggleUserMenu(): void {
    this.userMenuOpen.update((open) => !open);
    this.departmentMenuOpen.set(false);
  }

  protected selectDepartment(id: string): void {
    this.departments.select(id);
    this.departmentMenuOpen.set(false);
  }

  /**
   * The three preference writes.
   *
   * Each goes through the preferences store rather than straight to its own store, so the choice is persisted
   * against the person as well as applied here. The bar's own FR/EN/ES buttons call this same method — a language
   * picked with one press should be as durable as one picked from the menu.
   */
  protected setLanguage(language: Language): void {
    void this.preferences.set({ language });
  }

  protected setTheme(theme: Theme): void {
    void this.preferences.set({ theme });
  }

  /** The bar's one-press toggle, persisted like everything else rather than living only in this browser. */
  protected toggleTheme(): void {
    this.setTheme(this.theme.theme() === 'dark' ? 'light' : 'dark');
  }

  /**
   * Focus mode, from the control §02.2 requires to be visible in *both* modes.
   *
   * Closing the menus first because Focus mode is about to remove the affordances that opened them: a department
   * menu left hanging over a shell that no longer has a department switcher is a menu nothing can close.
   */
  protected toggleFocus(): void {
    this.closeMenus();
    void this.preferences.set({ focusMode: !this.focus.enabled() });
  }

  protected setTimeZone(timeZone: string): void {
    void this.preferences.set({ timeZone });
  }

  protected closeMenus(): void {
    this.departmentMenuOpen.set(false);
    this.userMenuOpen.set(false);
  }

  /**
   * Departments are keyed, not named — the same department reads differently in fr/en/es. Falls back to the code
   * when a dictionary has no entry yet, which is better than rendering a raw translation key at someone.
   */
  protected departmentLabel(
    translate: (key: string) => string,
    department: DepartmentScope | null | undefined,
  ): string {
    if (!department) {
      return translate('shell.noDepartment');
    }

    const label = translate(department.nameKey);

    return label === department.nameKey ? department.code.toUpperCase() : label;
  }

  protected logout(): void {
    this.session.logout();
  }
}
