import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import type { ActionBlock } from '@macro-deck/runtime';
import { ActionClipboardService } from '../../../../services/action-clipboard.service';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ActionCardFrameComponent } from './action-card-frame.component';

function fakeApiService(): ApiService {
  const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification']);
  apiSpy.onNotification.and.callFake(() => new Subject());
  Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
  return apiSpy;
}

describe('ActionCardFrameComponent enable toggle', () => {
  let fixture: ComponentFixture<ActionCardFrameComponent>;
  let store: ActionFlowStore;

  const block: ActionBlock = {
    id: 'block-1',
    type: 'action',
    blockType: 'system.run',
    label: 'Run Application',
    color: '#000',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionCardFrameComponent],
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    }).compileComponents();

    store = TestBed.inject(ActionFlowStore);
    store.flows.set([{ triggerId: 't', triggerType: 'onShortPress', children: [{ ...block }] }]);

    fixture = TestBed.createComponent(ActionCardFrameComponent);
    fixture.componentRef.setInput('block', store.flows()[0].children[0]);
    fixture.detectChanges();
  });

  function toggleInput(): HTMLInputElement {
    return fixture.nativeElement.querySelector('.action-enable-toggle input') as HTMLInputElement;
  }

  it('shows the switch on for an enabled block', () => {
    expect(toggleInput().checked).toBeTrue();
  });

  it('switches the block off through the store', () => {
    toggleInput().click();

    expect(store.flows()[0].children[0].disabled).toBeTrue();
  });

  it('marks a disabled card and labels it in the header', () => {
    fixture.componentRef.setInput('block', { ...block, disabled: true });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.action-card.disabled')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.action-disabled-tag')?.textContent?.trim()).toBe('Off');
    expect(toggleInput().checked).toBeFalse();
  });

  it('does not expand the card when the switch is used', () => {
    expect(store.isExpanded('block-1')).toBeFalse();

    toggleInput().click();

    expect(store.isExpanded('block-1')).toBeFalse();
  });

  it('does not expand the card on a keypress inside the header', () => {
    toggleInput().dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));

    expect(store.isExpanded('block-1')).toBeFalse();
  });

  it('still expands the card on a keypress on the header itself', () => {
    const header = fixture.nativeElement.querySelector('.action-card-header') as HTMLElement;
    header.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    expect(store.isExpanded('block-1')).toBeTrue();
  });

  it('carries the type color without repeating it as a header dot', () => {
    expect(fixture.nativeElement.querySelector('.action-color-indicator')).toBeNull();
    expect(fixture.nativeElement.querySelector('.action-card.action-color-action')).not.toBeNull();
  });
});

describe('ActionCardFrameComponent clipboard', () => {
  let fixture: ComponentFixture<ActionCardFrameComponent>;
  let store: ActionFlowStore;
  let clipboard: ActionClipboardService;

  const block: ActionBlock = {
    id: 'block-1',
    type: 'action',
    blockType: 'system.run',
    label: 'Run Application',
    color: '#000',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionCardFrameComponent],
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    }).compileComponents();

    store = TestBed.inject(ActionFlowStore);
    clipboard = TestBed.inject(ActionClipboardService);
    clipboard.clear();
    store.previewScope.set('widget');
    store.previewScopeRefId.set('widget-a');
    store.flows.set([{ triggerId: 't', triggerType: 'onShortPress', children: [{ ...block }] }]);

    fixture = TestBed.createComponent(ActionCardFrameComponent);
    fixture.componentRef.setInput('block', store.flows()[0].children[0]);
    fixture.detectChanges();
  });

  function header(): HTMLElement {
    return fixture.nativeElement.querySelector('.action-card-header') as HTMLElement;
  }

  function menuLabels(): string[] {
    return Array.from(document.querySelectorAll('.menu-item')).map(item => item.textContent!.trim());
  }

  function menuItem(label: string): HTMLButtonElement {
    return Array.from(document.querySelectorAll<HTMLButtonElement>('.menu-item'))
      .find(item => item.textContent!.trim() === label)!;
  }

  function openMenu(): void {
    header().dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 10, clientY: 10 }));
    fixture.detectChanges();
  }

  it('offers the clipboard actions on right-click', () => {
    openMenu();

    expect(menuLabels()).toEqual(['Copy', 'Cut', 'Duplicate', 'Paste after', 'Delete']);
  });

  it('disables Paste while the clipboard is empty', () => {
    openMenu();

    expect(menuItem('Paste after').disabled).toBeTrue();

    clipboard.copy(block);
    fixture.detectChanges();

    expect(menuItem('Paste after').disabled).toBeFalse();
  });

  it('copies through the menu', () => {
    openMenu();
    menuItem('Copy').click();

    expect(clipboard.entry()!.block.id).toBe('block-1');
    expect(clipboard.entry()!.isCut).toBeFalse();
  });

  it('cuts through the menu and dims the source', () => {
    openMenu();
    menuItem('Cut').click();
    fixture.detectChanges();

    expect(clipboard.entry()!.isCut).toBeTrue();
    expect(fixture.nativeElement.querySelector('.action-card.cut-source')).not.toBeNull();
  });

  it('offers the cut source a way back out of the move', () => {
    openMenu();
    menuItem('Cut').click();
    fixture.detectChanges();

    openMenu();
    menuItem('Cancel move').click();
    fixture.detectChanges();

    expect(clipboard.entry()).toBeNull();
    expect(fixture.nativeElement.querySelector('.action-card.cut-source')).toBeNull();
  });

  it('copies and cuts from the focused header', () => {
    header().dispatchEvent(new KeyboardEvent('keydown', { key: 'c', ctrlKey: true, bubbles: true }));
    expect(clipboard.entry()!.isCut).toBeFalse();

    header().dispatchEvent(new KeyboardEvent('keydown', { key: 'x', metaKey: true, bubbles: true }));
    expect(clipboard.entry()!.isCut).toBeTrue();
  });

  it('does not toggle the card on a clipboard shortcut', () => {
    header().dispatchEvent(new KeyboardEvent('keydown', { key: 'c', ctrlKey: true, bubbles: true }));

    expect(store.isExpanded('block-1')).toBeFalse();
  });

  it('drops Duplicate from the header but keeps it in the right-click menu', () => {
    expect(fixture.nativeElement.querySelector('[aria-label="Duplicate action"]')).toBeNull();

    openMenu();
    expect(menuLabels()).toEqual(['Copy', 'Cut', 'Duplicate', 'Paste after', 'Delete']);
  });
});

describe('ActionCardFrameComponent comment', () => {
  let fixture: ComponentFixture<ActionCardFrameComponent>;
  let store: ActionFlowStore;

  const block: ActionBlock = {
    id: 'block-1',
    type: 'action',
    blockType: 'system.run',
    label: 'Run Application',
    color: '#000',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ActionCardFrameComponent],
      providers: [
        provideZonelessChangeDetection(),
        ActionFlowStore,
        { provide: ApiService, useValue: fakeApiService() },
      ],
    }).compileComponents();

    store = TestBed.inject(ActionFlowStore);
    store.flows.set([{ triggerId: 't', triggerType: 'onShortPress', children: [{ ...block }] }]);

    fixture = TestBed.createComponent(ActionCardFrameComponent);
    fixture.componentRef.setInput('block', store.flows()[0].children[0]);
    fixture.detectChanges();
  });

  function commentButton(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.action-comment-button') as HTMLButtonElement;
  }

  function popoverTextarea(): HTMLTextAreaElement | null {
    return fixture.nativeElement.querySelector('shared-overlay-panel textarea');
  }

  async function openPopover(): Promise<void> {
    commentButton().click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('opens the popover seeded with the block current comment', async () => {
    fixture.componentRef.setInput('block', { ...block, comment: 'Existing note' });
    fixture.detectChanges();

    await openPopover();

    expect(popoverTextarea()?.value).toBe('Existing note');
  });

  it('writes typed text through to the store live, with no Save button', async () => {
    await openPopover();

    const textarea = popoverTextarea()!;
    textarea.value = 'A helpful note';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(store.flows()[0].children[0].comment).toBe('A helpful note');
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    expect(buttons.some(b => /save/i.test(b.textContent ?? ''))).toBeFalse();
  });

  it('shows .has-comment only when a comment exists', () => {
    expect(commentButton().classList.contains('has-comment')).toBeFalse();

    fixture.componentRef.setInput('block', { ...block, comment: 'A note' });
    fixture.detectChanges();

    expect(commentButton().classList.contains('has-comment')).toBeTrue();
  });

  it('does not expand or collapse the card when clicked', () => {
    expect(store.isExpanded('block-1')).toBeFalse();

    commentButton().click();
    fixture.detectChanges();

    expect(store.isExpanded('block-1')).toBeFalse();
  });

  it('closes on Escape and returns focus to the button', async () => {
    await openPopover();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(popoverTextarea()).toBeNull();
    expect(document.activeElement).toBe(commentButton());
  });

  it('labels the button "Add comment" without a comment and with the text when one exists', () => {
    expect(commentButton().getAttribute('aria-label')).toBe('Add comment');

    fixture.componentRef.setInput('block', { ...block, comment: 'Retry on failure' });
    fixture.detectChanges();

    expect(commentButton().getAttribute('aria-label')).toBe('Edit comment: Retry on failure');
  });
});
