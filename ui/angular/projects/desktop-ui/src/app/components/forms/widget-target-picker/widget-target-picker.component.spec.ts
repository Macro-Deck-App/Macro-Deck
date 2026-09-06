import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';

import { WIDGET_TARGET_SELF, WidgetTargetPickerComponent } from './widget-target-picker.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('WidgetTargetPickerComponent', () => {
  let fixture: ComponentFixture<WidgetTargetPickerComponent>;
  let component: WidgetTargetPickerComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WidgetTargetPickerComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(WidgetTargetPickerComponent);
    component = fixture.componentInstance;
  });

  function render(inputs: Partial<WidgetTargetPickerComponent>): void {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
  }

  it('shows "This widget" inside a widget flow', () => {
    render({ value: WIDGET_TARGET_SELF, hasOwnerWidget: true });

    const chip = fixture.nativeElement.querySelector('.target-chip');
    expect(chip?.textContent).toContain('This widget');
    expect(fixture.nativeElement.querySelector('shared-combobox')).toBeNull();
  });

  it('shows the picker instead of the chip where there is no owning widget', () => {
    render({ value: WIDGET_TARGET_SELF, hasOwnerWidget: false });

    expect(fixture.nativeElement.querySelector('.target-chip')).toBeNull();
    expect(fixture.nativeElement.querySelector('shared-combobox')).not.toBeNull();
  });

  it('clears to an empty target rather than back to itself', () => {
    render({ value: WIDGET_TARGET_SELF, hasOwnerWidget: true });
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));

    fixture.nativeElement.querySelector('.chip-clear').click();

    expect(emitted).toEqual(['']);
  });

  it('offers a way back to the owning widget once cleared', () => {
    render({ value: '', hasOwnerWidget: true });
    const emitted: string[] = [];
    component.valueChange.subscribe(value => emitted.push(value));

    fixture.nativeElement.querySelector('.target-self').click();

    expect(emitted).toEqual([WIDGET_TARGET_SELF]);
  });

  it('does not offer "use this widget" where there is no owning widget', () => {
    render({ value: '', hasOwnerWidget: false });

    expect(fixture.nativeElement.querySelector('.target-self')).toBeNull();
  });

  it('flags a target that matches no widget', () => {
    render({
      value: 'deleted-widget',
      options: [{ value: 'other-widget', label: 'Other' }],
    });

    expect(fixture.nativeElement.querySelector('.target-error')).not.toBeNull();
  });

  it('accepts a target that matches a widget', () => {
    render({
      value: 'a-widget',
      options: [{ value: 'a-widget', label: 'Mute (Main / Home)' }],
    });

    expect(fixture.nativeElement.querySelector('.target-error')).toBeNull();
  });

  it('does not flag a target while its options are still unknown', () => {
    render({ value: 'a-widget', options: [], loading: true });
    expect(fixture.nativeElement.querySelector('.target-error')).toBeNull();

    render({ loading: false });
    expect(fixture.nativeElement.querySelector('.target-error')).toBeNull();
  });
});
