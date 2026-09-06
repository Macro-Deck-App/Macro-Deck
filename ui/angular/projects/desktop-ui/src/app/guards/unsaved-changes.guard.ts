import { CanDeactivateFn } from '@angular/router';

export interface ConfirmsNavigation {
  confirmNavigation(): Promise<boolean> | boolean;
}

export const unsavedChangesGuard: CanDeactivateFn<Partial<ConfirmsNavigation>> =
  component => component?.confirmNavigation?.() ?? true;
