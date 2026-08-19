import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { UpperCasePipe } from '@angular/common';
import { TranslocoDirective } from '@jsverse/transloco';
import { DepartmentScopeStore } from '../../core/scope/department-scope.store';
import { Language, LanguageStore } from '../../core/i18n/language.store';
import { LayoutStore } from '../../core/layout/layout.store';
import { SessionStore } from '../../core/session/session.store';
import { ThemeStore } from '../../core/theme/theme.store';
import { formatDayMonth, workWeek } from '../../core/time/week';

@Component({
  selector: 'app-top-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, UpperCasePipe],
  templateUrl: './top-bar.html',
  styleUrl: './top-bar.scss',
})
export class TopBar {
  protected readonly layout = inject(LayoutStore);
  protected readonly session = inject(SessionStore);
  protected readonly theme = inject(ThemeStore);
  protected readonly language = inject(LanguageStore);
  protected readonly departments = inject(DepartmentScopeStore);

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

  protected toggleUserMenu(): void {
    this.userMenuOpen.update((open) => !open);
    this.departmentMenuOpen.set(false);
  }

  protected selectDepartment(id: string): void {
    this.departments.select(id);
    this.departmentMenuOpen.set(false);
  }

  protected setLanguage(language: Language): void {
    this.language.set(language);
  }

  protected closeMenus(): void {
    this.departmentMenuOpen.set(false);
    this.userMenuOpen.set(false);
  }

  protected logout(): void {
    void this.session.logout();
  }
}
