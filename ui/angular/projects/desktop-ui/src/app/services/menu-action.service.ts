import { Injectable, inject } from '@angular/core';

import { shellBridge } from '../util/shell-bridge';

import { SettingsModalService } from './settings-modal.service';

@Injectable({ providedIn: 'root' })
export class MenuActionService {
  private readonly settingsModal = inject(SettingsModalService);

  private started = false;

  start(): void {
    const bridge = shellBridge();
    if (this.started || !bridge?.onMenuAction) {
      return;
    }

    this.started = true;
    void bridge.onMenuAction(event => this.apply(event.action));
    void bridge.takeMenuAction?.().then(action => this.apply(action));
  }

  apply(action: string | null): void {
    if (action === 'settings') {
      this.settingsModal.open();
    }
  }
}
