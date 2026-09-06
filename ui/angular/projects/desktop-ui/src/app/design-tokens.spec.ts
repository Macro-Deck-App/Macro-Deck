import { Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ButtonComponent, InputComponent } from '@shared';
import { ComboboxComponent } from './components/forms/combobox/combobox.component';
import { DateTimePickerComponent } from './components/forms/datetime-picker/datetime-picker.component';
import { DurationInputComponent } from './components/forms/duration-input/duration-input.component';
import { HotkeyRecorderComponent } from './components/forms/hotkey-recorder/hotkey-recorder.component';
import { MultiSelectComponent } from './components/forms/multi-select/multi-select.component';
import { SelectComponent } from './components/forms/select/select.component';
import { VariableTextInputComponent } from './components/forms/variable-text-input/variable-text-input.component';
import { provideLocalizationTesting } from '../testing/localization-test-support';

describe('design tokens', () => {
  let probe: HTMLElement;

  beforeEach(() => {
    probe = document.createElement('div');
    probe.style.position = 'absolute';
    document.body.appendChild(probe);
  });

  afterEach(() => probe.remove());

  function renderedPx(token: string): number {
    probe.style.width = `var(${token})`;
    return parseFloat(getComputedStyle(probe).width);
  }

  it('leaves the root font size to the browser', () => {
    expect(document.documentElement.style.fontSize).toBe('');
    expect(getComputedStyle(document.documentElement).fontSize).toBe('16px');
  });

  it('renders the type scale at its documented sizes', () => {
    expect(renderedPx('--text-xs')).toBe(12);
    expect(renderedPx('--text-sm')).toBe(12);
    expect(renderedPx('--text-base')).toBe(13);
    expect(renderedPx('--text-md')).toBe(14);
    expect(renderedPx('--text-lg')).toBe(17);
    expect(renderedPx('--text-xl')).toBe(21);
  });

  it('keeps the type scale rising with no size below the caption floor', () => {
    const scale = ['--text-xs', '--text-sm', '--text-base', '--text-md', '--text-lg', '--text-xl'].map(renderedPx);

    expect(scale[0]).toBeGreaterThanOrEqual(12);
    for (let i = 1; i < scale.length; i++) {
      expect(scale[i]).withContext(`step ${i}`).toBeGreaterThanOrEqual(scale[i - 1]);
    }
  });

  it('renders the spacing scale at whole pixels', () => {
    expect(renderedPx('--space-1')).toBe(3);
    expect(renderedPx('--space-2')).toBe(6);
    expect(renderedPx('--space-3')).toBe(10);
    expect(renderedPx('--space-4')).toBe(13);
    expect(renderedPx('--space-5')).toBe(20);
    expect(renderedPx('--space-6')).toBe(26);
    expect(renderedPx('--space-8')).toBe(39);
  });

  it('renders the radii and the control and navigation heights at whole pixels', () => {
    expect(renderedPx('--radius-sm')).toBe(5);
    expect(renderedPx('--radius-md')).toBe(6);
    expect(renderedPx('--radius-lg')).toBe(10);
    expect(renderedPx('--radius-xl')).toBe(13);
    expect(renderedPx('--control-height')).toBe(38);
    expect(renderedPx('--nav-item-height')).toBe(44);
  });

  it('no longer declares an interface scale', () => {
    expect(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale')).toBe('');
  });
});

const CONTROLS = [
  ComboboxComponent,
  DateTimePickerComponent,
  DurationInputComponent,
  HotkeyRecorderComponent,
  InputComponent,
  MultiSelectComponent,
  SelectComponent,
  VariableTextInputComponent,
  ButtonComponent,
] as const;

const CONTROL_BOXES: Record<string, string> = {
  'shared-input': 'input.control',
  'shared-select': 'button.control',
  'shared-combobox': '.cb-input',
  'shared-multi-select': '.ms-trigger',
  'shared-duration-input': '.du-input',
  'shared-datetime-picker': '.dt-input',
  'shared-hotkey-recorder': '.hk-field',
  'shared-variable-text-input': '.vti-editor',
  'shared-button': 'button',
};

@Component({
  standalone: true,
  imports: [...CONTROLS],
  template: `
    <shared-input />
    <shared-select />
    <shared-combobox />
    <shared-multi-select />
    <shared-duration-input />
    <shared-datetime-picker />
    <shared-hotkey-recorder />
    <shared-variable-text-input />
    <shared-button>Label</shared-button>
  `,
})
class StandardControlsHostComponent {}

@Component({
  standalone: true,
  imports: [InputComponent, VariableTextInputComponent],
  template: `
    <shared-input [multiline]="true" [rows]="3" />
    <shared-variable-text-input [multiline]="true" [rows]="3" />
  `,
})
class GrowableControlsHostComponent {}

describe('control geometry', () => {
  function render(host: unknown): { heights: Record<string, number>; destroy: () => void } {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [host as never],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });

    const fixture = TestBed.createComponent(host as never);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const heights: Record<string, number> = {};
    for (const [tag, box] of Object.entries(CONTROL_BOXES)) {
      const element = root.querySelector(`${tag} ${box}`);
      expect(element).withContext(tag).not.toBeNull();
      heights[tag] = (element as HTMLElement).getBoundingClientRect().height;
    }
    return { heights, destroy: () => fixture.destroy() };
  }

  function intrinsicPx(element: HTMLElement): number {
    const style = getComputedStyle(element);
    return parseFloat(style.lineHeight)
      + parseFloat(style.paddingTop) + parseFloat(style.paddingBottom)
      + parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
  }

  it('holds every standard single-line control at one identical height', () => {
    const { heights, destroy } = render(StandardControlsHostComponent);

    const [firstTag, ...otherTags] = Object.keys(heights);
    expect(heights[firstTag]).withContext(firstTag).toBeGreaterThan(0);
    for (const tag of otherTags) {
      expect(heights[tag]).withContext(tag).toBe(heights[firstTag]);
    }

    destroy();
  });

  it('renders searchable and plain dropdowns at the same height', () => {
    const { heights, destroy } = render(StandardControlsHostComponent);

    expect(heights['shared-combobox']).toBe(heights['shared-select']);
    expect(heights['shared-combobox']).toBe(38);

    destroy();
  });

  it('leaves the control height binding, with the intrinsic box under the token', () => {
    const standard = render(StandardControlsHostComponent);
    const standardBox = document.querySelector('shared-input input.control') as HTMLElement;
    expect(intrinsicPx(standardBox)).toBeLessThan(38);
    standard.destroy();
  });

  it('does not clamp controls that are meant to grow', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [GrowableControlsHostComponent],
      providers: [provideZonelessChangeDetection(), ...provideLocalizationTesting()],
    });

    const fixture = TestBed.createComponent(GrowableControlsHostComponent);
    fixture.detectChanges();

    const textarea = fixture.nativeElement.querySelector('shared-input textarea.control') as HTMLElement;
    const chipEditor = fixture.nativeElement.querySelector('shared-variable-text-input .vti-editor') as HTMLElement;

    expect(textarea.getBoundingClientRect().height).toBeGreaterThan(38);
    expect(chipEditor.getBoundingClientRect().height).toBeGreaterThan(38);

    fixture.destroy();
  });
});
