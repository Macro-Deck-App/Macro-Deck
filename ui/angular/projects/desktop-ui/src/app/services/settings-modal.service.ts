import { Injectable, signal } from '@angular/core';

export type SettingsCategory =
  | 'account'
  | 'appearance'
  | 'startup'
  | 'language'
  | 'security'
  | 'network'
  | 'devices'
  | 'client-targets'
  | 'adb'
  | 'backups'
  | 'migration'
  | 'logging'
  | 'developer'
  | 'about';

const DEFAULT_CATEGORY: SettingsCategory = 'appearance';

@Injectable({
  providedIn: 'root',
})
export class SettingsModalService {
  readonly isOpen = signal(false);
  readonly activeCategory = signal<SettingsCategory>(DEFAULT_CATEGORY);

  open(category: SettingsCategory = DEFAULT_CATEGORY): void {
    this.activeCategory.set(category);
    this.isOpen.set(true);
  }

  close(): void {
    this.isOpen.set(false);
  }

  setCategory(category: SettingsCategory): void {
    this.activeCategory.set(category);
  }
}
