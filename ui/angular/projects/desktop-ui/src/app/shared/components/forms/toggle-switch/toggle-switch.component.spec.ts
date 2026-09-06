import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ToggleSwitchComponent } from './toggle-switch.component';

@Component({
  standalone: true,
  imports: [ToggleSwitchComponent, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<shared-toggle-switch [formControl]="control" />',
})
class FormControlHostComponent {
  readonly control = new FormControl(false, { nonNullable: true });
}

describe('ToggleSwitchComponent', () => {
  let fixture: ComponentFixture<ToggleSwitchComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ToggleSwitchComponent, FormControlHostComponent],
      providers: [provideZonelessChangeDetection()],
    }).compileComponents();

    fixture = TestBed.createComponent(ToggleSwitchComponent);
    fixture.detectChanges();
  });

  function input(): HTMLInputElement {
    return fixture.nativeElement.querySelector('.ts-input');
  }

  it('toggles and emits the new value on change', () => {
    const emitted: boolean[] = [];
    fixture.componentInstance.changed.subscribe(value => emitted.push(value));

    input().click();
    fixture.detectChanges();

    expect(fixture.componentInstance.checkedState()).toBeTrue();
    expect(emitted).toEqual([true]);
  });

  it('ignores changes while disabled', () => {
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();

    const emitted: boolean[] = [];
    fixture.componentInstance.changed.subscribe(value => emitted.push(value));
    fixture.componentInstance.toggle();

    expect(fixture.componentInstance.checkedState()).toBeFalse();
    expect(emitted).toEqual([]);
  });

  it('shows a spinner and ignores changes while busy', () => {
    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ts-spinner')).toBeTruthy();

    const emitted: boolean[] = [];
    fixture.componentInstance.changed.subscribe(value => emitted.push(value));
    input().click();
    fixture.detectChanges();

    expect(fixture.componentInstance.checkedState()).toBeFalse();
    expect(input().checked).toBeFalse();
    expect(emitted).toEqual([]);
  });

  it('hides the spinner and accepts changes again once no longer busy', () => {
    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();
    fixture.componentRef.setInput('busy', false);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ts-spinner')).toBeNull();

    input().click();
    fixture.detectChanges();

    expect(fixture.componentInstance.checkedState()).toBeTrue();
  });

  it('repaints when writeValue flips the state programmatically under zoneless OnPush', async () => {
    fixture.componentInstance.writeValue(true);
    await fixture.whenStable();

    expect(input().checked).toBeTrue();
    expect(fixture.nativeElement.querySelector('.ts')?.classList).toContain('ts-checked');
  });

  it('repaints when setDisabledState disables it programmatically under zoneless OnPush', async () => {
    fixture.componentInstance.setDisabledState(true);
    await fixture.whenStable();

    expect(input().disabled).toBeTrue();
    expect(fixture.nativeElement.querySelector('.ts')?.classList).toContain('ts-disabled');
  });

  it('repaints when a bound FormControl value changes programmatically', async () => {
    const hostFixture = TestBed.createComponent(FormControlHostComponent);
    hostFixture.detectChanges();
    const hostInput = hostFixture.nativeElement.querySelector('.ts-input') as HTMLInputElement;
    expect(hostInput.checked).toBeFalse();

    hostFixture.componentInstance.control.setValue(true);
    await hostFixture.whenStable();

    expect(hostInput.checked).toBeTrue();
    expect(hostFixture.nativeElement.querySelector('.ts')?.classList).toContain('ts-checked');
  });

  describe('thumb geometry', () => {
    const ROOT_FONT_SIZES = ['12px', '16px', '20px', '24px'];
    const TOLERANCE = 0.5;

    beforeEach(() => {
      document.documentElement.style.setProperty('--color-border', '#000000');
      document.documentElement.style.setProperty('--transition-fast', '0s');
    });

    afterEach(() => {
      document.documentElement.style.removeProperty('--color-border');
      document.documentElement.style.removeProperty('--transition-fast');
      document.documentElement.style.removeProperty('font-size');
    });

    function insets(): { top: number; right: number; bottom: number; left: number } {
      const track = fixture.nativeElement.querySelector('.ts-track') as HTMLElement;
      const thumb = fixture.nativeElement.querySelector('.ts-thumb') as HTMLElement;
      const trackRect = track.getBoundingClientRect();
      const thumbRect = thumb.getBoundingClientRect();
      return {
        top: thumbRect.top - trackRect.top,
        right: trackRect.right - thumbRect.right,
        bottom: trackRect.bottom - thumbRect.bottom,
        left: thumbRect.left - trackRect.left,
      };
    }

    for (const rootFontSize of ROOT_FONT_SIZES) {
      it(`centers the thumb vertically when unchecked at a ${rootFontSize} root`, () => {
        document.documentElement.style.fontSize = rootFontSize;
        fixture.detectChanges();

        const { top, bottom, left } = insets();
        expect(Math.abs(top - bottom))
          .withContext(`root ${rootFontSize}: top vs bottom`)
          .toBeLessThan(TOLERANCE);
        expect(Math.abs(left - top))
          .withContext(`root ${rootFontSize}: left vs top`)
          .toBeLessThan(TOLERANCE);
      });

      it(`centers the thumb vertically and mirrors the left inset when checked at a ${rootFontSize} root`, () => {
        document.documentElement.style.fontSize = rootFontSize;
        fixture.detectChanges();
        const { left: uncheckedLeft } = insets();

        fixture.componentRef.setInput('checked', true);
        fixture.detectChanges();

        const { top, bottom, right } = insets();
        expect(Math.abs(top - bottom))
          .withContext(`root ${rootFontSize}: top vs bottom (checked)`)
          .toBeLessThan(TOLERANCE);
        expect(Math.abs(right - uncheckedLeft))
          .withContext(`root ${rootFontSize}: checked right inset vs unchecked left inset`)
          .toBeLessThan(TOLERANCE);
      });
    }
  });
});
