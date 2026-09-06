import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FolderContextMenuComponent } from './folder-context-menu.component';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';

describe('FolderContextMenuComponent', () => {
  let fixture: ComponentFixture<FolderContextMenuComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [FolderContextMenuComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(FolderContextMenuComponent);
  });

  function itemIds(): string[] {
    return fixture.componentInstance.menuItems().map(item => item.id);
  }

  it('offers export, import and the outliner move actions alongside the folder actions', () => {
    expect(itemIds()).toEqual([
      'new-folder', 'import-inside', 'move-up', 'move-down', 'move-into', 'move-to-parent',
      'rename', 'duplicate', 'export', 'focus-rule', 'delete',
    ]);
  });

  it('labels "Automatic activation…" plainly with no rules configured', () => {
    expect(fixture.componentInstance.menuItems().find(item => item.id === 'focus-rule')?.label)
      .toBe('Automatic activation…');
  });

  it('suffixes "Automatic activation…" with the rule count once the folder has rules', async () => {
    fixture.componentRef.setInput('focusRuleCount', 2);
    await fixture.whenStable();

    expect(fixture.componentInstance.menuItems().find(item => item.id === 'focus-rule')?.label)
      .toBe('Automatic activation… (2)');
  });

  it('adds "Set as start folder" only when the folder can become the start folder', async () => {
    fixture.componentRef.setInput('canSetStart', true);
    await fixture.whenStable();

    expect(itemIds()).toContain('set-start');
    expect(fixture.componentInstance.menuItems().find(item => item.id === 'set-start')?.label)
      .toBe('Set as start folder');
  });

  it('disables delete when the folder cannot be deleted, and keeps export available', async () => {
    fixture.componentRef.setInput('canDelete', false);
    await fixture.whenStable();

    const items = fixture.componentInstance.menuItems();

    expect(items.find(item => item.id === 'delete')?.disabled).toBeTrue();
    expect(items.find(item => item.id === 'export')?.disabled).toBeFalsy();
  });

  it('the four move items default to disabled', () => {
    const items = fixture.componentInstance.menuItems();

    expect(items.find(item => item.id === 'move-up')?.disabled).toBeTrue();
    expect(items.find(item => item.id === 'move-down')?.disabled).toBeTrue();
    expect(items.find(item => item.id === 'move-into')?.disabled).toBeTrue();
    expect(items.find(item => item.id === 'move-to-parent')?.disabled).toBeTrue();
  });

  it('each move item enables independently through its own input', async () => {
    fixture.componentRef.setInput('canMoveUp', true);
    fixture.componentRef.setInput('canMoveDown', true);
    fixture.componentRef.setInput('canMoveInto', true);
    fixture.componentRef.setInput('canMoveToParent', true);
    await fixture.whenStable();

    const items = fixture.componentInstance.menuItems();

    expect(items.find(item => item.id === 'move-up')?.disabled).toBeFalse();
    expect(items.find(item => item.id === 'move-down')?.disabled).toBeFalse();
    expect(items.find(item => item.id === 'move-into')?.disabled).toBeFalse();
    expect(items.find(item => item.id === 'move-to-parent')?.disabled).toBeFalse();
  });
});
