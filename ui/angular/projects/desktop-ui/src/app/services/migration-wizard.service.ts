import { Injectable, signal } from '@angular/core';
import { MigrationSourceInput } from './migration.service';

export interface MigrationSourceChoice {
  sourceId: string;
  sourceName: string;
  input: MigrationSourceInput;
}

@Injectable({ providedIn: 'root' })
export class MigrationWizardService {
  readonly isOpen = signal(false);

  private choice: MigrationSourceChoice | null = null;

  open(choice: MigrationSourceChoice): void {
    this.choice = choice;
    this.isOpen.set(true);
  }

  takeChoice(): MigrationSourceChoice | null {
    const choice = this.choice;
    this.choice = null;
    return choice;
  }

  close(): void {
    this.isOpen.set(false);
  }
}
