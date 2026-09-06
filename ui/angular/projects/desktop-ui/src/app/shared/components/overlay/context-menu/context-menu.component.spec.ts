import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ContextMenuComponent, ContextMenuItem } from './context-menu.component';

describe('ContextMenuComponent', () => {
  let fixture: ComponentFixture<ContextMenuComponent>;
  let component: ContextMenuComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContextMenuComponent],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(ContextMenuComponent);
    component = fixture.componentInstance;
    document.body.appendChild(fixture.nativeElement);
    jasmine.clock().install();
    jasmine.clock().mockDate();
  });

  afterEach(() => {
    jasmine.clock().uninstall();
    fixture.nativeElement.remove();
  });

  function menuButtons(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll(':scope > shared-overlay-panel > div.op-panel > button.menu-item'));
  }

  function flyoutButtons(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.submenu button.menu-item'));
  }

  function open(items: ContextMenuItem[]): void {
    fixture.componentRef.setInput('items', items);
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    // The outer panel's pop-in animation gives it a transform while it plays, which makes it the
    // containing block for the flyout and moves the flyout out to the body. The component defers
    // opening a flyout past that window (see its class doc), but this suite drives the deferral
    // with a mocked clock, which the CSS animation does not follow. Settling it here puts the
    // panel in the state the deferral exists to wait for.
    for (const panel of document.querySelectorAll('.op-panel')) {
      panel.getAnimations().forEach(animation => animation.finish());
    }
  }

  it('existing flat-item consumers behave unchanged: a plain item click emits its id and closes', () => {
    const clicks: string[] = [];
    let closedCount = 0;
    component.itemClick.subscribe(id => clicks.push(id));
    component.closed.subscribe(() => closedCount++);

    open([{ id: 'edit', label: 'Edit' }, { id: 'delete', label: 'Delete', danger: true }]);

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(clicks).toEqual(['edit']);
    expect(closedCount).toBe(1);
  });

  it('a disabled flat item ignores a click', () => {
    const clicks: string[] = [];
    component.itemClick.subscribe(id => clicks.push(id));

    open([{ id: 'paste', label: 'Paste', disabled: true }]);
    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(clicks).toEqual([]);
  });


  it('a parent click neither emits an item id nor closes the menu', () => {
    const clicks: string[] = [];
    let closedCount = 0;
    component.itemClick.subscribe(id => clicks.push(id));
    component.closed.subscribe(() => closedCount++);

    open([{
      id: 'pin',
      label: 'Pin widget',
      children: [
        { id: 'pin-profile', label: 'Every folder in this profile' },
        { id: 'pin-subtree', label: 'This folder and its subfolders' },
      ],
    }]);

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    jasmine.clock().tick(200);
    fixture.detectChanges();

    expect(clicks).toEqual([]);
    expect(closedCount).toBe(0);
    expect(component.openChildId()).toBe('pin');
    expect(flyoutButtons().map(b => b.textContent?.trim())).toEqual([
      'Every folder in this profile',
      'This folder and its subfolders',
    ]);
  });

  it('clicking the open parent again closes its own flyout without emitting or closing the menu', () => {
    open([{
      id: 'pin',
      label: 'Pin widget',
      children: [{ id: 'pin-profile', label: 'Every folder in this profile' }],
    }]);

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    jasmine.clock().tick(200);
    fixture.detectChanges();
    expect(component.openChildId()).toBe('pin');

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(component.openChildId()).toBeNull();
  });

  it('a child click emits the flat child id and closes the whole menu', () => {
    const clicks: string[] = [];
    let closedCount = 0;
    component.itemClick.subscribe(id => clicks.push(id));
    component.closed.subscribe(() => closedCount++);

    open([{
      id: 'pin',
      label: 'Pin widget',
      children: [
        { id: 'pin-profile', label: 'Every folder in this profile' },
        { id: 'pin-subtree', label: 'This folder and its subfolders' },
      ],
    }]);

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    jasmine.clock().tick(200);
    fixture.detectChanges();

    flyoutButtons()[1].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(clicks).toEqual(['pin-subtree']);
    expect(closedCount).toBe(1);
    expect(component.openChildId()).toBeNull();
  });

  it('clicking inside the open flyout does not dismiss the outer menu', () => {
    let closedCount = 0;
    component.closed.subscribe(() => closedCount++);

    open([{
      id: 'pin',
      label: 'Pin widget',
      children: [{ id: 'pin-profile', label: 'Every folder in this profile' }],
    }]);

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    jasmine.clock().tick(200);
    fixture.detectChanges();

    // A pointerdown/mousedown anywhere inside the flyout (not necessarily on a button) must not be
    // treated as "outside" the outer panel - the flyout is a DOM descendant of it (issue #245 trap).
    const flyoutPanel = fixture.nativeElement.querySelector('.submenu .op-panel') as HTMLElement;
    expect(flyoutPanel).not.toBeNull();
    flyoutPanel.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    fixture.detectChanges();

    expect(closedCount).toBe(0);
    expect(component.openChildId()).toBe('pin');
  });

  it('control: a mousedown truly outside the menu does dismiss it', () => {
    let closedCount = 0;
    component.closed.subscribe(() => closedCount++);

    open([{
      id: 'pin',
      label: 'Pin widget',
      children: [{ id: 'pin-profile', label: 'Every folder in this profile' }],
    }]);
    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    jasmine.clock().tick(200);
    fixture.detectChanges();

    document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    fixture.detectChanges();

    expect(closedCount).toBe(1);
    expect(component.openChildId()).toBeNull();
  });

  it('renders an active child with a checked state and an inactive one without it', () => {
    open([{
      id: 'pin',
      label: 'Pinned',
      children: [
        { id: 'pin-profile', label: 'Every folder in this profile', active: false },
        { id: 'pin-subtree', label: 'This folder and its subfolders', active: true },
      ],
    }]);

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    jasmine.clock().tick(200);
    fixture.detectChanges();

    const [profileBtn, subtreeBtn] = flyoutButtons();
    expect(profileBtn.getAttribute('aria-checked')).toBe('false');
    expect(subtreeBtn.getAttribute('aria-checked')).toBe('true');
  });

  it('closes any open flyout when the menu itself closes', () => {
    open([{
      id: 'pin',
      label: 'Pin widget',
      children: [{ id: 'pin-profile', label: 'Every folder in this profile' }],
    }]);
    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    jasmine.clock().tick(200);
    fixture.detectChanges();
    expect(component.openChildId()).toBe('pin');

    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();

    expect(component.openChildId()).toBeNull();
  });

  it('opening the flyout is delayed while the outer panel is still animating, and opens once the window passes', () => {
    open([{
      id: 'pin',
      label: 'Pin widget',
      children: [{ id: 'pin-profile', label: 'Every folder in this profile' }],
    }]);

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect(component.openChildId()).withContext('not yet open during the animation window').toBeNull();

    jasmine.clock().tick(200);
    fixture.detectChanges();
    expect(component.openChildId()).toBe('pin');
  });

  it('opens immediately once the animation window has already passed', () => {
    open([{
      id: 'pin',
      label: 'Pin widget',
      children: [{ id: 'pin-profile', label: 'Every folder in this profile' }],
    }]);
    jasmine.clock().tick(200); // let the outer panel's pop-in animation window pass before interacting

    menuButtons()[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(component.openChildId()).toBe('pin');
  });
});
