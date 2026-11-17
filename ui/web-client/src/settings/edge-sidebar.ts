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
  repaint(): void;
}

interface OpenSidebar {
  backdrop: HTMLElement;
  panel: HTMLElement;
  button: HTMLButtonElement;
}

export function createEdgeSidebar(options: EdgeSidebarOptions): EdgeSidebarHandle {
  let opened: OpenSidebar | null = null;

  function onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      close();
      return;
    }
    if (event.key === 'Tab' && opened !== null) {
      event.preventDefault();
      opened.button.focus();
    }
  }

  function paint(sidebar: OpenSidebar): void {
    sidebar.panel.setAttribute('aria-label', options.translate(ClientAppStrings.WebClient.Settings.Sidebar));
    sidebar.button.setAttribute(
      'aria-label',
      options.translate(ClientAppStrings.WebClient.Settings.OpenSettings));
  }

  function build(): OpenSidebar {
    const layer = document.createElement('div');
    layer.className = 'wc-edge-sidebar-backdrop';
    layer.addEventListener('click', event => {
      if (event.target === layer) close();
    });

    const panel = document.createElement('div');
    panel.className = 'wc-edge-sidebar';
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('aria-modal', 'true');
    layer.appendChild(panel);

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'wc-edge-sidebar-button';
    button.appendChild(gearIcon());
    button.addEventListener('click', () => {
      close();
      options.openSettings();
    });
    panel.appendChild(button);

    const sidebar: OpenSidebar = { backdrop: layer, panel: panel, button: button };
    paint(sidebar);
    return sidebar;
  }

  function open(): void {
    if (opened !== null) return;
    opened = build();
    options.container.appendChild(opened.backdrop);
    document.addEventListener('keydown', onKeydown);
    opened.button.focus();
  }

  function close(): void {
    const sidebar = opened;
    if (sidebar === null) return;
    opened = null;
    document.removeEventListener('keydown', onKeydown);
    if (sidebar.backdrop.parentNode !== null) sidebar.backdrop.parentNode.removeChild(sidebar.backdrop);
  }

  return {
    open: open,
    close: close,
    isOpen: () => opened !== null,
    repaint: () => {
      if (opened !== null) paint(opened);
    },
  };
}
