import { createEdgeSidebar, type EdgeSidebarHandle } from './edge-sidebar';

describe('edge sidebar', () => {
  let container: HTMLElement;
  let outside: HTMLButtonElement;
  let sidebar: EdgeSidebarHandle;

  beforeEach(() => {
    container = document.createElement('div');
    outside = document.createElement('button');
    container.appendChild(outside);
    document.body.appendChild(container);
    sidebar = createEdgeSidebar({ container, translate: key => key, openSettings: () => undefined });
  });

  afterEach(() => {
    sidebar.close();
    container.remove();
  });

  function tab(): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    document.activeElement!.dispatchEvent(event);
    return event;
  }

  it('is announced as a modal dialog', () => {
    sidebar.open();

    expect(container.querySelector('[role="dialog"]')!.getAttribute('aria-modal')).toBe('true');
  });

  it('keeps keyboard focus on its settings button instead of the deck behind it', () => {
    sidebar.open();
    const button = container.querySelector<HTMLButtonElement>('.wc-edge-sidebar-button')!;

    expect(document.activeElement).toBe(button);
    expect(tab().defaultPrevented).toBeTrue();
    expect(document.activeElement).toBe(button);
  });

  it('leaves Tab alone once it is closed', () => {
    sidebar.open();
    sidebar.close();
    outside.focus();

    expect(tab().defaultPrevented).toBeFalse();
  });
});
