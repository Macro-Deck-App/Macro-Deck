import { offersPasteMenu } from './paste-target.util';

function contextMenuOn(target: Element): MouseEvent {
  const event = new MouseEvent('contextmenu');
  Object.defineProperty(event, 'target', { value: target });
  return event;
}

describe('offersPasteMenu', () => {
  let list: HTMLElement;

  beforeEach(() => {
    list = document.createElement('div');
    list.className = 'action-list';
    list.innerHTML = `
      <div class="action-card-wrapper">
        <button class="drag-handle"></button>
        <div class="action-card">
          <div class="action-card-header"></div>
          <div class="action-card-body"><input class="param" /></div>
        </div>
      </div>`;
    document.body.appendChild(list);
  });

  afterEach(() => list.remove());

  it('offers the menu on the list itself', () => {
    expect(offersPasteMenu(contextMenuOn(list))).toBeTrue();
  });

  it('leaves a parameter field its own text menu', () => {
    expect(offersPasteMenu(contextMenuOn(list.querySelector('.param')!))).toBeFalse();
  });

  it('leaves a card to its own menu', () => {
    expect(offersPasteMenu(contextMenuOn(list.querySelector('.action-card-header')!))).toBeFalse();
    expect(offersPasteMenu(contextMenuOn(list.querySelector('.action-card-body')!))).toBeFalse();
  });
});
