import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WidgetContextMenuAction, WidgetContextMenuComponent } from './widget-context-menu.component';
import { provideLocalizationTesting } from '../../localization/localization-test-support';

describe('WidgetContextMenuComponent', () => {
  let fixture: ComponentFixture<WidgetContextMenuComponent>;
  let component: WidgetContextMenuComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(WidgetContextMenuComponent);
    component = fixture.componentInstance;
  });

  it('offers edit/copy/cut/pin/export/delete for a widget target', () => {
    fixture.componentRef.setInput('mode', 'widget');
    fixture.detectChanges();

    expect(component.menuItems().map(i => i.id)).toEqual(['edit', 'copy', 'cut', 'pin', 'export', 'delete']);
    expect(component.menuItems().find(i => i.id === 'delete')?.danger).toBeTrue();
  });

  it('I1: offers Unpin and both scope choices, the active one matching the current scope, and drops Cut for a pinned widget', () => {
    fixture.componentRef.setInput('mode', 'widget');
    fixture.componentRef.setInput('isPinned', true);
    fixture.componentRef.setInput('pinScope', 'Subtree');
    fixture.detectChanges();

    const items = component.menuItems();
    expect(items.map(i => i.id)).toEqual(['edit', 'copy', 'pin', 'unpin', 'export', 'delete']);
    expect(items.find(i => i.id === 'cut')).toBeUndefined();

    const scopeChoices = items.find(i => i.id === 'pin')!.children!;
    expect(scopeChoices.map(c => c.id)).toEqual(['pin-profile', 'pin-subtree']);
    expect(scopeChoices.find(c => c.id === 'pin-subtree')?.active).toBeTrue();
    expect(scopeChoices.find(c => c.id === 'pin-profile')?.active).toBeFalse();
  });

  it('I2: offers a reachable pin entry with no scope marked active for an unpinned widget', () => {
    fixture.componentRef.setInput('mode', 'widget');
    fixture.componentRef.setInput('isPinned', false);
    fixture.detectChanges();

    const items = component.menuItems();
    expect(items.map(i => i.id)).toContain('pin');
    expect(items.find(i => i.id === 'unpin')).toBeUndefined();
    expect(items.find(i => i.id === 'cut')).toBeDefined();

    const scopeChoices = items.find(i => i.id === 'pin')!.children!;
    expect(scopeChoices.every(c => !c.active)).toBeTrue();
  });

  it('offers paste and import for an empty cell', () => {
    fixture.componentRef.setInput('mode', 'empty');
    fixture.detectChanges();

    expect(component.menuItems().map(i => i.id)).toEqual(['paste', 'import']);
    expect(component.menuItems().find(i => i.id === 'import')?.label).toBe('Import widgets…');
  });

  it('disables paste when the clipboard is empty', () => {
    fixture.componentRef.setInput('mode', 'empty');
    fixture.componentRef.setInput('canPaste', false);
    fixture.detectChanges();

    expect(component.menuItems()[0].disabled).toBeTrue();
  });

  it('enables paste when the clipboard has content', () => {
    fixture.componentRef.setInput('mode', 'empty');
    fixture.componentRef.setInput('canPaste', true);
    fixture.detectChanges();

    expect(component.menuItems()[0].disabled).toBeFalse();
  });

  it('re-emits the clicked item id as a typed action', () => {
    const actions: WidgetContextMenuAction[] = [];
    component.action.subscribe(a => actions.push(a));

    component.onItemClick('cut');

    expect(actions).toEqual(['cut']);
  });

  it('I3: a scope choice re-emits as its own distinct action, never as plain pin or unpin', () => {
    const actions: WidgetContextMenuAction[] = [];
    component.action.subscribe(a => actions.push(a));

    component.onItemClick('pin-subtree');

    expect(actions).toEqual(['pin-subtree']);
    expect(actions).not.toContain('unpin');
  });

  describe('multi-selection (issue #213)', () => {
    it('omits edit and export and offers pluralised batch actions for a 2+ selection', () => {
      fixture.componentRef.setInput('mode', 'widget');
      fixture.componentRef.setInput('selectionCount', 3);
      fixture.detectChanges();

      const items = component.menuItems();
      expect(items.map(i => i.id)).toEqual(['copy', 'cut', 'pin', 'delete']);
      expect(items.find(i => i.id === 'copy')?.label).toBe('Copy 3 widgets');
      expect(items.find(i => i.id === 'delete')?.label).toBe('Delete 3 widgets');
      expect(items.find(i => i.id === 'delete')?.danger).toBeTrue();

      const scopeChoices = items.find(i => i.id === 'pin')!.children!;
      expect(scopeChoices.map(c => c.id)).toEqual(['pin-profile', 'pin-subtree']);
    });

    it('offers Unpin for a 2+ selection only when any member is pinned', () => {
      fixture.componentRef.setInput('mode', 'widget');
      fixture.componentRef.setInput('selectionCount', 2);
      fixture.componentRef.setInput('isPinned', false);
      fixture.detectChanges();
      expect(component.menuItems().find(i => i.id === 'unpin')).toBeUndefined();

      fixture.componentRef.setInput('isPinned', true);
      fixture.detectChanges();
      expect(component.menuItems().find(i => i.id === 'unpin')).toBeDefined();
    });

    it('a single-widget selection (selectionCount of 1) keeps the unchanged single-target menu', () => {
      fixture.componentRef.setInput('mode', 'widget');
      fixture.componentRef.setInput('selectionCount', 1);
      fixture.detectChanges();

      expect(component.menuItems().map(i => i.id)).toEqual(['edit', 'copy', 'cut', 'pin', 'export', 'delete']);
    });

    it('defaults to the single-target menu when selectionCount is not provided', () => {
      fixture.componentRef.setInput('mode', 'widget');
      fixture.detectChanges();

      expect(component.menuItems().map(i => i.id)).toEqual(['edit', 'copy', 'cut', 'pin', 'export', 'delete']);
    });
  });
});
