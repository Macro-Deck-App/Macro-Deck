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

  it('stops recording when the mode switches to the key dropdown', () => {
    arm();
    fixture.componentInstance.setMode('select');
    fixture.detectChanges();
    press({ key: 'p', code: 'KeyP' });

    expect(emitted).toEqual([]);
    expect(fixture.componentInstance.recording()).toBeFalse();
  });
});
