import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { KeyboardComboValue } from '@macro-deck/runtime';
import { KeyboardComboEditorComponent } from './keyboard-combo-editor.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('KeyboardComboEditorComponent', () => {
  let fixture: ComponentFixture<KeyboardComboEditorComponent>;
  let emitted: KeyboardComboValue[];

  function arm(): void {
    const field = fixture.nativeElement.querySelector('.kce-record') as HTMLButtonElement;
    field.click();
    fixture.detectChanges();
  }

  function press(init: KeyboardEventInit): void {
    document.dispatchEvent(new KeyboardEvent('keydown', { ...init, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [KeyboardComboEditorComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(KeyboardComboEditorComponent);
    emitted = [];
    fixture.componentInstance.valueChange.subscribe(value => emitted.push(value));
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('records the key and the modifiers held with it', () => {
    arm();
    press({ key: 'p', code: 'KeyP', metaKey: true, shiftKey: true });

    expect(emitted).toEqual([{ modifiers: ['Shift', 'Meta'], key: 'P' }]);
    expect(fixture.componentInstance.recording()).toBeFalse();
  });

  it('cancels on Escape without recording anything', () => {
    arm();
    press({ key: 'Escape', code: 'Escape' });

    expect(emitted).toEqual([]);
    expect(fixture.componentInstance.recording()).toBeFalse();
  });

  it('stops recording when a modifier is toggled instead', () => {
    arm();
    const toggle = fixture.nativeElement.querySelector('.kce-mod') as HTMLButtonElement;
    toggle.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    toggle.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.recording()).toBeFalse();

    press({ key: 'p', code: 'KeyP' });
    expect(emitted.length).toBe(1);
  });

  describe('for a field a plugin owns', () => {
    it('records the generic modifier even when the right-hand key was pressed', () => {
      arm();
      press({ key: 'Alt', code: 'AltRight', altKey: true });
      press({ key: 'F1', code: 'F1', altKey: true });

      expect(emitted).toEqual([{ modifiers: ['Alt'], key: 'F1' }]);
    });

    it('offers only the generic modifier toggles', () => {
      expect(fixture.nativeElement.querySelectorAll('.kce-mod').length).toBe(4);
    });
  });

  describe('for the host keyboard actions', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('sidedModifiers', true);
      fixture.detectChanges();
    });

    it('records the right-hand modifier that was held', () => {
      arm();
      press({ key: 'Alt', code: 'AltRight', altKey: true });
      press({ key: 'F1', code: 'F1', altKey: true });

      expect(emitted).toEqual([{ modifiers: ['RightAlt'], key: 'F1' }]);
    });

    it('records the generic modifier for the left-hand key', () => {
      arm();
      press({ key: 'Control', code: 'ControlLeft', ctrlKey: true });
      press({ key: 'k', code: 'KeyK', ctrlKey: true });

      expect(emitted).toEqual([{ modifiers: ['Ctrl'], key: 'K' }]);
    });

    it('records AltGr as the keys Windows sends for it, left Ctrl and right Alt', () => {
      arm();
      press({ key: 'Control', code: 'ControlLeft', ctrlKey: true });
      press({ key: 'AltGraph', code: 'AltRight', ctrlKey: true, altKey: true });
      expect(fixture.componentInstance.recording()).toBeTrue();

      press({ key: 'q', code: 'KeyQ', ctrlKey: true, altKey: true });

      expect(emitted).toEqual([{ modifiers: ['Ctrl', 'RightAlt'], key: 'Q' }]);
    });

    it('records right Alt when the platform reports AltGr without the Alt flag', () => {
      arm();
      press({ key: 'AltGraph', code: 'AltRight' });
      press({ key: 'q', code: 'KeyQ' });

      expect(emitted).toEqual([{ modifiers: ['RightAlt'], key: 'Q' }]);
    });

    it('offers a toggle for each right-hand modifier', () => {
      const toggles = Array.from(fixture.nativeElement.querySelectorAll('.kce-mod')) as HTMLButtonElement[];
      expect(toggles.length).toBe(8);

      toggles.find(toggle => toggle.textContent?.trim() === 'Right Alt')!.click();

      expect(emitted).toEqual([{ modifiers: ['RightAlt'], key: '' }]);
    });
  });

  it('stops recording when the mode switches to the key dropdown', () => {
    arm();
    fixture.componentInstance.setMode('select');
    fixture.detectChanges();
    press({ key: 'p', code: 'KeyP' });

    expect(emitted).toEqual([]);
    expect(fixture.componentInstance.recording()).toBeFalse();
  });
});
