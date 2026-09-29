import { ClientAppStrings } from '@macro-deck/runtime';
import { gearIcon } from './client-settings';

export interface EdgeSidebarOptions {
  container: HTMLElement;
  translate(qualifiedKey: string): string;
  openSettings(): void;
}

export interface EdgeSidebarHandle {
  open(): void;
  close(): void;
  isOpen(): boolean;
}

export function createEdgeSidebar(options: EdgeSidebarOptions): EdgeSidebarHandle {
  let backdrop: HTMLElement | null = null;

  function onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      close();
      return;
    }
    if (event.key === 'Tab' && backdrop !== null) {
      event.preventDefault();
      const button = backdrop.querySelector('button');
      if (button !== null) button.focus();
    }
  }

  function build(): HTMLElement {
    const layer = document.createElement('div');
    layer.className = 'wc-edge-sidebar-backdrop';
    layer.addEventListener('click', event => {
      if (event.target === layer) close();
    });

    const panel = document.createElement('div');
    panel.className = 'wc-edge-sidebar';
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('aria-modal', 'true');
    panel.setAttribute('aria-label', options.translate(ClientAppStrings.WebClient.Settings.Sidebar));
    layer.appendChild(panel);

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'wc-edge-sidebar-button';
    button.setAttribute('aria-label', options.translate(ClientAppStrings.WebClient.Settings.OpenSettings));
    button.appendChild(gearIcon());
    button.addEventListener('click', () => {
      close();
      options.openSettings();
    });
    panel.appendChild(button);

    return layer;
  }

  function open(): void {
    if (backdrop !== null) return;
    backdrop = build();
    options.container.appendChild(backdrop);
    document.addEventListener('keydown', onKeydown);
    const button = backdrop.querySelector('button');
    if (button !== null) button.focus();
  }

  function close(): void {
    const layer = backdrop;
    if (layer === null) return;
    backdrop = null;
    document.removeEventListener('keydown', onKeydown);
    if (layer.parentNode !== null) layer.parentNode.removeChild(layer);
  }

  return {
    open: open,
    close: close,
    isOpen: () => backdrop !== null,
  };
}
