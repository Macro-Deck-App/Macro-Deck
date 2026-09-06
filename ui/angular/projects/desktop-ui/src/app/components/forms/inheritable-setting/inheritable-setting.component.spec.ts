import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { InheritableSettingComponent } from './inheritable-setting.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

@Component({
  standalone: true,
  imports: [InheritableSettingComponent],
  template: `
    <shared-inheritable-setting
      [label]="label()"
      [value]="value()"
      [effectiveValue]="effectiveValue()"
      [unit]="unit()"
      [resettable]="resettable()"
      [inheritedLabel]="inheritedLabel()"
      [fallbackLabel]="fallbackLabel()"
      (reset)="resetCount.set(resetCount() + 1)">
      <input class="projected-slider" type="range">
    </shared-inheritable-setting>
  `,
})
class HostComponent {
  label = signal('Columns');
  value = signal<number | null>(null);
  effectiveValue = signal<number | null>(null);
  unit = signal('');
  resettable = signal(true);
  inheritedLabel = signal('Inherited');
  fallbackLabel = signal('Default');
  resetCount = signal(0);
}

describe('InheritableSettingComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  function valueText(): string {
    return fixture.nativeElement.querySelector('.setting-value').textContent.trim();
  }

  function resetButton(): HTMLButtonElement | null {
    return fixture.nativeElement.querySelector('.setting-reset');
  }

  it('shows a plain overridden value with no unit', async () => {
    fixture.componentInstance.value.set(5);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(valueText()).toBe('5');
  });

  it('appends the unit to an overridden value', async () => {
    fixture.componentInstance.unit.set('px');
    fixture.componentInstance.value.set(20);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(valueText()).toBe('20 px');
  });

  it('shows the inherited label with the resolved value while inheriting', async () => {
    fixture.componentInstance.effectiveValue.set(12);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(valueText()).toBe('Inherited · 12');
  });

  it('shows the fallback label when both value and effective value are null', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(valueText()).toBe('Default');
  });

  it('always renders the reset button, disabled while inherited', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const button = resetButton();
    expect(button).toBeTruthy();
    expect(button!.disabled).toBeTrue();
  });

  it('enables the reset button once a value overrides the inherited one', async () => {
    fixture.componentInstance.value.set(8);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(resetButton()!.disabled).toBeFalse();
  });

  it('emits reset when the enabled button is clicked', async () => {
    fixture.componentInstance.value.set(8);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    resetButton()!.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.resetCount()).toBe(1);
  });

  it('hides the reset button entirely when resettable is false', async () => {
    fixture.componentInstance.resettable.set(false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(resetButton()).toBeNull();
  });

  it('projects the slider content below the header row', () => {
    expect(fixture.nativeElement.querySelector('.projected-slider')).toBeTruthy();
  });
});
