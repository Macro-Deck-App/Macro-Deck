import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HotkeyValue } from '@macro-deck/runtime';
import { HotkeyRecorderComponent } from './hotkey-recorder.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('HotkeyRecorderComponent', () => {
  describe('recording', () => {
    let fixture: ComponentFixture<HotkeyRecorderComponent>;
    let emitted: (HotkeyValue | null)[];

    function arm(): void {
      const field = fixture.nativeElement.querySelector('.hk-field') as HTMLButtonElement;
      field.click();
      fixture.detectChanges();
    }

    function press(init: KeyboardEventInit): void {
      document.dispatchEvent(new KeyboardEvent('keydown', { ...init, bubbles: true, cancelable: true }));
      fixture.detectChanges();
    }

    beforeEach(() => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [HotkeyRecorderComponent],
        providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
      });
      fixture = TestBed.createComponent(HotkeyRecorderComponent);
      emitted = [];
      fixture.componentInstance.valueChange.subscribe(value => emitted.push(value));
      fixture.detectChanges();
    });

    afterEach(() => fixture.destroy());

    it('records a combination pressed while the button is not focused', () => {
      arm();
      expect(fixture.componentInstance.recording()).toBeTrue();

      press({ key: 'k', code: 'KeyK', ctrlKey: true, shiftKey: true });

      expect(emitted).toEqual([{ modifiers: ['Ctrl', 'Shift'], key: 'K', code: 'KeyK' }]);
      expect(fixture.componentInstance.recording()).toBeFalse();
    });

    it('cancels on Escape without recording anything', () => {
      arm();
      press({ key: 'Escape', code: 'Escape' });

      expect(emitted).toEqual([]);
      expect(fixture.componentInstance.recording()).toBeFalse();
    });

    it('ignores keys pressed before the recorder was armed', () => {
      press({ key: 'k', code: 'KeyK' });

      expect(emitted).toEqual([]);
    });
  });
});
