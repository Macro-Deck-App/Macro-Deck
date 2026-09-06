import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MultiSelectComponent } from './multi-select.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('MultiSelectComponent', () => {
  let fixture: ComponentFixture<MultiSelectComponent>;
  let component: MultiSelectComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MultiSelectComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(MultiSelectComponent);
    component = fixture.componentInstance;
    component.options = [
      { value: 'living-room-lamp', label: 'Living Room Lamp' },
      { value: 'kitchen-light', label: 'Kitchen Light' },
      { value: 'garage-door', label: 'Garage Door' },
    ];
  });

  it('shows every option when the filter is empty', () => {
    expect(component.filteredOptions()).toEqual(component.options);
  });

  it('narrows options by a case-insensitive match on the label', () => {
    component.filterText.set('LIGHT');
    expect(component.filteredOptions().map(o => o.value)).toEqual(['kitchen-light']);
  });

  it('narrows options by a match on the value when the label misses', () => {
    component.options = [{ value: 'sensor.living_room_temp', label: 'Living Room Temperature' }];
    component.filterText.set('sensor.living_room');
    expect(component.filteredOptions().length).toBe(1);
  });

  it('keeps selection state for an option hidden by the filter', () => {
    component.value = ['garage-door'];
    component.filterText.set('light');

    expect(component.filteredOptions().map(o => o.value)).not.toContain('garage-door');
    expect(component.isSelected('garage-door')).toBeTrue();
  });

  it('restores the full list once the filter is cleared', () => {
    component.filterText.set('light');
    expect(component.filteredOptions().length).toBe(1);

    component.filterText.set('');
    expect(component.filteredOptions()).toEqual(component.options);
  });

  it('renders a filter input and narrows the rendered checkboxes when typed into', async () => {
    fixture.componentRef.setInput('options', component.options);
    component.isOpen.set(true);
    fixture.detectChanges();
    await fixture.whenStable();

    const filterInput = fixture.nativeElement.querySelector('input.ms-filter') as HTMLInputElement;
    expect(filterInput).toBeTruthy();
    expect(fixture.nativeElement.querySelectorAll('shared-checkbox.ms-option').length).toBe(3);

    filterInput.value = 'garage';
    filterInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.nativeElement.querySelectorAll('shared-checkbox.ms-option').length).toBe(1);
  });

  it('lets an unbreakable option label wrap instead of widening the panel', async () => {
    component.options = [{ value: 'x', label: 'EXTERNAL (binary_sensor.external_securityzone_2)' }];
    fixture.componentRef.setInput('options', component.options);
    component.isOpen.set(true);
    fixture.detectChanges();
    await fixture.whenStable();

    const label = fixture.nativeElement.querySelector('shared-checkbox.ms-option .cb-label') as HTMLElement;
    expect(getComputedStyle(label).overflowWrap).toBe('anywhere');
  });

  it('emits the filter and stops filtering locally when the source is server-side', () => {
    component.filterLocally = false;
    const emitted: string[] = [];
    component.filterChange.subscribe(f => emitted.push(f));

    component.onFilterInput('light');

    expect(emitted).toEqual(['light']);
    expect(component.filteredOptions()).toEqual(component.options);
  });

  it('does not emit a filter when filtering locally', () => {
    const emitted: string[] = [];
    component.filterChange.subscribe(f => emitted.push(f));

    component.onFilterInput('light');

    expect(emitted).toEqual([]);
    expect(component.filteredOptions().map(o => o.value)).toEqual(['kitchen-light']);
  });

  it('clears the filter when the popup closes', () => {
    component.filterText.set('light');
    component.close();
    expect(component.filterText()).toBe('');
  });
});
