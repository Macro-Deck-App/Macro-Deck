import { Component, provideZonelessChangeDetection, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SelectComponent, SelectOption } from './select.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

@Component({
  standalone: true,
  imports: [SelectComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<shared-select [options]="options" placeholder="Pick…" />`,
})
class HostComponent {
  options: SelectOption[] = [
    { value: 'a', label: 'A' },
    { value: 'b', label: 'B' },
    { value: 'c', label: 'C', disabled: true },
  ];
}

describe('SelectComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  function instance(): SelectComponent {
    return fixture.debugElement.children[0].componentInstance as SelectComponent;
  }

  function trigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('button.control');
  }

  function optionButtons(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.sel-option'));
  }

  it('renders the placeholder while no value is set', () => {
    expect(trigger().textContent).toContain('Pick…');
  });

  it('shows the selected option label on the trigger', () => {
    instance().writeValue('b');
    fixture.detectChanges();
    expect(trigger().textContent).toContain('B');
  });

  it('opens the listbox on click and renders all options', () => {
    trigger().click();
    fixture.detectChanges();
    expect(instance().isOpen()).toBeTrue();
    expect(optionButtons().length).toBe(3);
  });

  it('emits through the registered onChange when an option is picked', () => {
    const changes: Array<string | number | null> = [];
    instance().registerOnChange(v => changes.push(v));

    trigger().click();
    fixture.detectChanges();
    optionButtons()[1].click();
    fixture.detectChanges();

    expect(changes).toEqual(['b']);
    expect(instance().value).toBe('b');
    expect(instance().isOpen()).toBeFalse();
  });

  it('ignores disabled options', () => {
    const changes: Array<string | number | null> = [];
    instance().registerOnChange(v => changes.push(v));
    instance().pick(fixture.componentInstance.options[2]);
    expect(changes).toEqual([]);
  });

  it('supports keyboard selection with arrows and Enter', () => {
    const changes: Array<string | number | null> = [];
    instance().registerOnChange(v => changes.push(v));

    trigger().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    fixture.detectChanges();
    expect(instance().isOpen()).toBeTrue();

    trigger().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    trigger().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();

    expect(changes).toEqual(['b']);
  });

  it('emits opened when the listbox opens', () => {
    let openedCount = 0;
    instance().opened.subscribe(() => openedCount++);
    trigger().click();
    fixture.detectChanges();
    expect(openedCount).toBe(1);
  });
});

@Component({
  standalone: true,
  imports: [SelectComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<shared-select [options]="options" placeholder="Pick…" />`,
})
class TypeaheadHostComponent {
  options: SelectOption[] = [
    { value: 'apple', label: 'Apple' },
    { value: 'banana', label: 'Banana' },
    { value: 'blue-berry', label: 'Blue berry' },
    { value: 'raspberry', label: 'Raspberry' },
    { value: 'rhubarb', label: 'Rhubarb', disabled: true },
    { value: 'rocket', label: 'Rocket' },
  ];
}

describe('SelectComponent type-ahead', () => {
  let fixture: ComponentFixture<TypeaheadHostComponent>;

  beforeEach(() => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(0));
    TestBed.configureTestingModule({
      imports: [TypeaheadHostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(TypeaheadHostComponent);
    fixture.detectChanges();
  });

  afterEach(() => jasmine.clock().uninstall());

  function instance(): SelectComponent {
    return fixture.debugElement.children[0].componentInstance as SelectComponent;
  }

  function trigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('button.control');
  }

  function type(...keys: string[]): void {
    for (const key of keys) {
      trigger().dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    }
    fixture.detectChanges();
  }

  it('highlights the first option matching a typed character', () => {
    trigger().click();
    fixture.detectChanges();

    type('r');

    expect(instance().activeIndex()).toBe(3);
  });

  it('narrows the match as more characters are typed', () => {
    trigger().click();
    fixture.detectChanges();

    type('b', 'l');

    expect(instance().activeIndex()).toBe(2);
  });

  it('matches labels containing a space', () => {
    trigger().click();
    fixture.detectChanges();

    type('b', 'l', 'u', 'e', ' ', 'b');

    expect(instance().activeIndex()).toBe(2);
    expect(instance().isOpen()).toBeTrue();
  });

  it('cycles through the options starting with a repeated character', () => {
    trigger().click();
    fixture.detectChanges();

    type('r');
    expect(instance().activeIndex()).toBe(3);

    type('r');
    expect(instance().activeIndex()).toBe(5);

    type('r');
    expect(instance().activeIndex()).toBe(3);
  });

  it('starts a new search once the buffer has expired', () => {
    trigger().click();
    fixture.detectChanges();

    type('b');
    expect(instance().activeIndex()).toBe(1);

    jasmine.clock().tick(600);
    type('a');

    expect(instance().activeIndex()).toBe(0);
  });

  it('leaves the highlight alone when nothing matches', () => {
    trigger().click();
    fixture.detectChanges();

    type('b');
    type('z');

    expect(instance().activeIndex()).toBe(1);
  });

  it('opens the listbox on typing without committing a value', () => {
    const changes: Array<string | number | null> = [];
    instance().registerOnChange(v => changes.push(v));

    type('r');

    expect(instance().isOpen()).toBeTrue();
    expect(instance().activeIndex()).toBe(3);
    expect(changes).toEqual([]);
  });

  it('picks the typed match on Enter', () => {
    const changes: Array<string | number | null> = [];
    instance().registerOnChange(v => changes.push(v));

    trigger().click();
    fixture.detectChanges();
    type('r');
    type('Enter');

    expect(changes).toEqual(['raspberry']);
  });
});
