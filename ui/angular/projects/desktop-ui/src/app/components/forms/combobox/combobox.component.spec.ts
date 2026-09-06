import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ComboboxComponent } from './combobox.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('ComboboxComponent displayLabel', () => {
  let fixture: ComponentFixture<ComboboxComponent>;
  let component: ComboboxComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ComboboxComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
    fixture = TestBed.createComponent(ComboboxComponent);
    component = fixture.componentInstance;
  });

  it('shows the friendly label while idle and a fresh empty search on focus (picker mode)', () => {
    component.value = 'spotify:track:xyz';
    component.displayLabel = 'Bohemian Rhapsody';

    expect(component.shownText()).toBe('Bohemian Rhapsody');

    component.startEdit();
    expect(component.editing()).toBeTrue();
    // The opaque id must not reappear in the box - focusing starts a clean search instead.
    expect(component.shownText()).toBe('');
  });

  it('keeps the opaque value while searching and only searches, never committing keystrokes', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe(v => emitted.push(v));
    component.value = 'spotify:track:xyz';
    component.displayLabel = 'Bohemian Rhapsody';

    component.startEdit();
    component.onInput('queen');

    expect(component.shownText()).toBe('queen');
    expect(component.value).toBe('spotify:track:xyz');
    expect(emitted).toEqual([]);
  });

  it('seeds the search with the current value on focus in free-text mode', () => {
    component.value = 'notepad';
    component.displayLabel = '';

    component.startEdit();
    expect(component.shownText()).toBe('notepad');

    const emitted: string[] = [];
    component.valueChange.subscribe(v => emitted.push(v));
    component.onInput('firefox');
    expect(component.value).toBe('firefox');
    expect(emitted).toEqual(['firefox']);
  });

  it('shows the raw value when no label is provided', () => {
    component.value = 'raw';
    component.displayLabel = '';
    expect(component.shownText()).toBe('raw');
  });

  it('renders the friendly label into the input element while idle', async () => {
    fixture.componentRef.setInput('value', 'spotify:track:xyz');
    fixture.componentRef.setInput('displayLabel', 'Bohemian Rhapsody');
    fixture.detectChanges();
    await fixture.whenStable();

    const input = fixture.nativeElement.querySelector('input.cb-input') as HTMLInputElement;
    expect(input.value).toBe('Bohemian Rhapsody');
  });

  it('clears the opaque id from the input on focus instead of showing it (issue #13)', async () => {
    fixture.componentRef.setInput('value', 'spotify:track:xyz');
    fixture.componentRef.setInput('displayLabel', 'Bohemian Rhapsody');
    fixture.detectChanges();
    await fixture.whenStable();

    const input = fixture.nativeElement.querySelector('input.cb-input') as HTMLInputElement;
    expect(input.value).toBe('Bohemian Rhapsody');

    input.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(input.value).toBe('');
    expect(component.isOpen()).toBeTrue();
  });

  it('clearing empties the value and drops picker mode so typing is free text again (issue #136)', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe(v => emitted.push(v));
    component.value = 'spotify:track:xyz';
    component.displayLabel = 'Bohemian Rhapsody';
    component.startEdit();

    component.clear();

    expect(emitted).toEqual(['']);
    expect(component.value).toBe('');
    expect(component.editing()).toBeFalse();
    expect(component.isOpen()).toBeFalse();

    component.onInput('queen');
    expect(component.value).toBe('queen');
    expect(emitted).toEqual(['', 'queen']);
  });

  it('offers the clear button only while a clearable field holds a value', async () => {
    const clearButton = () => fixture.nativeElement.querySelector('button.cb-clear');

    fixture.componentRef.setInput('value', '');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(clearButton()).toBeNull();

    fixture.componentRef.setInput('value', 'spotify:track:xyz');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(clearButton()).not.toBeNull();

    fixture.componentRef.setInput('clearable', false);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(clearButton()).toBeNull();
  });

  it('clicking clear empties a picked value the input never exposes as text (issue #136)', async () => {
    const emitted: string[] = [];
    fixture.componentRef.setInput('value', 'spotify:track:xyz');
    fixture.componentRef.setInput('displayLabel', 'Bohemian Rhapsody');
    fixture.detectChanges();
    await fixture.whenStable();
    component.valueChange.subscribe(v => emitted.push(v));

    const input = fixture.nativeElement.querySelector('input.cb-input') as HTMLInputElement;
    input.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(input.value).toBe('');

    (fixture.nativeElement.querySelector('button.cb-clear') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(emitted).toEqual(['']);
  });

  it('opens and closes the suggestion list from the caret', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.isOpen()).toBeFalse();

    const caret = fixture.nativeElement.querySelector('.cb-caret') as HTMLElement;

    caret.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.isOpen()).toBeTrue();

    caret.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.isOpen()).toBeFalse();
  });

  it('picking an option emits its value and leaves edit mode so the label can show', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe(v => emitted.push(v));
    component.startEdit();

    component.pick({ value: 'spotify:track:xyz', label: 'Bohemian Rhapsody' });

    expect(emitted).toEqual(['spotify:track:xyz']);
    expect(component.editing()).toBeFalse();
    expect(component.isOpen()).toBeFalse();
  });
});

describe('ComboboxComponent keyword filtering', () => {
  let component: ComboboxComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ComboboxComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });
  });

  it('matches a hidden keyword though it is absent from the visible label', async () => {
    const fixture = TestBed.createComponent(ComboboxComponent);
    component = fixture.componentInstance;
    component.options = [
      { value: 'Europe/Berlin', label: 'Berlin (Europe/Berlin, UTC+02:00)', keywords: ['gmt+2'] },
      { value: 'Asia/Tokyo', label: 'Tokyo (Asia/Tokyo, UTC+09:00)', keywords: ['gmt+9'] },
    ];
    fixture.detectChanges();

    component.startEdit();
    component.onInput('gmt+2');

    expect(component.filteredOptions().length).toBe(1);
    expect(component.filteredOptions()[0].value).toBe('Europe/Berlin');

    fixture.detectChanges();
    await fixture.whenStable();
    const optionButtons = fixture.nativeElement.querySelectorAll('.cb-option');
    expect(optionButtons.length).toBe(1);
    expect((optionButtons[0] as HTMLElement).textContent?.trim()).toBe('Berlin (Europe/Berlin, UTC+02:00)');
  });

  it('matches keywords case-insensitively, ignoring whitespace on either side', () => {
    component = TestBed.createComponent(ComboboxComponent).componentInstance;
    // Labels deliberately free of the needles below, so only a keyword match can find them.
    component.options = [
      { value: 'Europe/Berlin', label: 'Berlin', keywords: ['utc+02:00'] },
      { value: 'America/New_York', label: 'New York', keywords: ['new york'] },
    ];
    component.startEdit();

    component.onInput('UTC+02');
    expect(component.filteredOptions().map(o => o.value)).toEqual(['Europe/Berlin']);

    component.onInput('utc +02');
    expect(component.filteredOptions().map(o => o.value)).toEqual(['Europe/Berlin']);

    component.onInput('newyork');
    expect(component.filteredOptions().map(o => o.value)).toEqual(['America/New_York']);
  });

  it('behaves exactly as before when no keywords are passed', () => {
    component = TestBed.createComponent(ComboboxComponent).componentInstance;
    component.options = [
      { value: 'notepad', label: 'Notepad' },
      { value: 'spotify:track:xyz', label: 'Bohemian Rhapsody' },
    ];

    component.startEdit();
    component.onInput('note');
    expect(component.filteredOptions().length).toBe(1);

    component.onInput('track');
    expect(component.filteredOptions().length).toBe(1);

    component.onInput('zzz');
    expect(component.filteredOptions().length).toBe(0);

    component.onInput('');
    expect(component.filteredOptions().length).toBe(2);

    component.filterLocally = false;
    component.onInput('zzz');
    expect(component.filteredOptions().length).toBe(2);
  });
});
