import { disableContextMenu } from './disable-context-menu';

describe('disableContextMenu', () => {
  const contextMenu = () => new Event('contextmenu', { cancelable: true, bubbles: true });

  it('cancels the browser context menu inside the target', () => {
    const target = document.createElement('div');
    disableContextMenu(target);

    const event = contextMenu();
    target.dispatchEvent(event);

    expect(event.defaultPrevented).toBeTrue();
  });

  it('cancels one raised on a descendant, which is where a long press lands', () => {
    const target = document.createElement('div');
    const child = document.createElement('img');
    target.appendChild(child);
    disableContextMenu(target);

    const event = contextMenu();
    child.dispatchEvent(event);

    expect(event.defaultPrevented).toBeTrue();
  });

  it('leaves a text entry its own menu, which is how a pointer-only user pastes', () => {
    const target = document.createElement('div');
    const field = document.createElement('input');
    target.appendChild(field);
    disableContextMenu(target);

    const event = contextMenu();
    field.dispatchEvent(event);

    expect(event.defaultPrevented).toBeFalse();
  });
});
