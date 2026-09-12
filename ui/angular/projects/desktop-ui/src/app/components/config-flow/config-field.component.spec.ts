import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActionParameterDef, ActionParameterType } from '@macro-deck/runtime';
import { ConfigFieldComponent } from './config-field.component';
import { WidgetTargetPickerComponent } from '../forms/widget-target-picker/widget-target-picker.component';
import { HotkeyRecorderComponent } from '../forms/hotkey-recorder/hotkey-recorder.component';
import { KeyboardComboEditorComponent } from '../forms/keyboard-combo-editor/keyboard-combo-editor.component';
import { KeyboardSequenceEditorComponent } from '../forms/keyboard-sequence-editor/keyboard-sequence-editor.component';
import { ActionOptionsService } from '../../services/action-options.service';
import { provideLocalizationTesting } from '../../../testing/localization-test-support';

function field(type: ActionParameterType, overrides: Partial<ActionParameterDef> = {}): ActionParameterDef {
  return { name: 'field', label: 'Field', type, required: false, ...overrides } as ActionParameterDef;
}

describe('ConfigFieldComponent', () => {
  let fixture: ComponentFixture<ConfigFieldComponent>;
  let component: ConfigFieldComponent;
  let optionsRequests: Record<string, unknown>[];

  beforeEach(() => {
    optionsRequests = [];
    TestBed.configureTestingModule({
      imports: [ConfigFieldComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: ActionOptionsService,
          useValue: {
            getOptions: (request: unknown) => {
              optionsRequests.push(request as Record<string, unknown>);
              return Promise.resolve({ options: [{ value: 'w1', label: 'Button (Default / Home)' }] });
            },
          },
        },
      ],
    });
    fixture = TestBed.createComponent(ConfigFieldComponent);
    component = fixture.componentInstance;
  });

  it('submits the structured combo a keyboard combo field records, not display text', () => {
    component.field = field(ActionParameterType.KeyboardCombo);
    const emitted: unknown[] = [];
    component.valueChange.subscribe(v => emitted.push(v));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('input.cf-input')).toBeNull();
    fixture.debugElement.query(By.directive(KeyboardComboEditorComponent))
      .componentInstance.valueChange.emit({ modifiers: [], key: '+' });

    expect(emitted).toEqual([{ modifiers: [], key: '+' }]);
  });

  it('pre-fills the combo editor with a stored combo', () => {
    component.field = field(ActionParameterType.KeyboardCombo);
    component.value = { modifiers: ['ctrl'], key: 'a' };
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.directive(KeyboardComboEditorComponent)).componentInstance.value)
      .toEqual({ modifiers: ['ctrl'], key: 'a' });
  });

  it('renders the hotkey recorder and keyboard sequence editor for their field types', () => {
    component.field = field(ActionParameterType.Hotkey);
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.directive(HotkeyRecorderComponent))).toBeTruthy();

    const sequenceFixture = TestBed.createComponent(ConfigFieldComponent);
    sequenceFixture.componentInstance.field = field(ActionParameterType.KeyboardSequence);
    sequenceFixture.detectChanges();
    expect(sequenceFixture.debugElement.query(By.directive(KeyboardSequenceEditorComponent))).toBeTruthy();
  });

  for (const [type, value] of [
    [ActionParameterType.Hotkey, '+'],
    [ActionParameterType.Hotkey, {}],
    [ActionParameterType.KeyboardCombo, '+'],
    [ActionParameterType.KeyboardSequence, '+'],
    [ActionParameterType.KeyboardSequence, { steps: {} }],
  ] as const) {
    it(`starts the ${type} editor empty for a value of the wrong shape (${JSON.stringify(value)})`, () => {
      component.field = field(type);
      component.value = value;
      expect(() => fixture.detectChanges()).not.toThrow();
      expect(fixture.nativeElement.querySelector('input.cf-input')).toBeNull();
    });
  }

  it('renders a text input for string fields', () => {
    component.field = field(ActionParameterType.String);
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input.cf-input');
    expect(input).toBeTruthy();
    expect(input.type).toBe('text');
  });

  it('renders a url input for url fields', () => {
    component.field = field(ActionParameterType.Url);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input.cf-input').type).toBe('url');
  });

  it('renders a masked secret input with a reveal toggle', () => {
    component.field = field(ActionParameterType.Password);
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input.cf-secret-input');
    expect(input.type).toBe('password');

    const reveal = fixture.nativeElement.querySelector('.cf-reveal button');
    reveal.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('input.cf-secret-input').type).toBe('text');
  });

  it('renders a select dropdown for choice fields', () => {
    component.field = field(ActionParameterType.Choice, {
      options: [{ value: 'a', label: 'A' }],
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('shared-select')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('shared-combobox')).toBeFalsy();
  });

  it('renders a shared checkbox for boolean fields', () => {
    component.field = field(ActionParameterType.Boolean, { description: 'Enable it' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('shared-checkbox')).toBeTruthy();
  });

  it('renders a shared-multi-select for MultiSelect fields and submits an array', () => {
    component.field = field(ActionParameterType.MultiSelect, {
      options: [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }],
    });
    component.value = ['a'];
    fixture.detectChanges();

    const multiSelect = fixture.nativeElement.querySelector('shared-multi-select');
    expect(multiSelect).toBeTruthy();

    const emitted: unknown[] = [];
    component.valueChange.subscribe(v => emitted.push(v));
    component.emit(['a', 'b']);
    expect(emitted).toEqual([['a', 'b']]);
  });

  it('parses number input and emits null for empty or invalid values', () => {
    component.field = field(ActionParameterType.Number);
    fixture.detectChanges();

    const emitted: unknown[] = [];
    component.valueChange.subscribe(v => emitted.push(v));

    component.emitNumber('42');
    component.emitNumber('');
    component.emitNumber('abc');

    expect(emitted).toEqual([42, null, null]);
  });

  it('shows the error message when set', () => {
    component.field = field(ActionParameterType.String);
    component.error = 'Required';
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.cf-error').textContent).toContain('Required');
  });

  it('renders macro decks own widget picker for widget-target fields', () => {
    component.field = field(ActionParameterType.WidgetTarget, {
      optionsSourceId: 'macrodeck.widgets',
    });
    fixture.detectChanges();

    // The component instance, not markup: the requirement is that it reuses the existing picker rather
    // than growing a second one.
    expect(fixture.debugElement.query(By.directive(WidgetTargetPickerComponent))).toBeTruthy();
    expect(fixture.nativeElement.querySelector('input.cf-input')).toBeNull();
  });

  it('never offers "this widget" inside a config flow', () => {
    component.field = field(ActionParameterType.WidgetTarget, {
      optionsSourceId: 'macrodeck.widgets',
      allowSelf: false,
    });
    component.value = '$self';
    fixture.detectChanges();

    const picker = fixture.debugElement.query(By.directive(WidgetTargetPickerComponent))
      .componentInstance as WidgetTargetPickerComponent;

    expect(picker.hasOwnerWidget).toBeFalse();
    // hasOwnerWidget is false by default, so on its own it proves nothing; this is the real assertion.
    expect(picker.pickerValue).toBe('');
  });

  it('asks the host for widget options by source id, carrying any type restriction', async () => {
    component.field = field(ActionParameterType.WidgetTarget, {
      optionsSourceId: 'macrodeck.widgets',
      widgetTypes: ['ActionButton'],
    });
    fixture.detectChanges();

    await component.loadWidgetOptions();

    expect(optionsRequests.length).toBe(1);
    expect(optionsRequests[0]['optionsSourceId']).toBe('macrodeck.widgets');
    expect(optionsRequests[0]['widgetTypes']).toEqual(['ActionButton']);
    expect(component.widgetOptions()).toEqual([{ value: 'w1', label: 'Button (Default / Home)' }]);
  });

  it('emits the picked widget id', () => {
    component.field = field(ActionParameterType.WidgetTarget, { optionsSourceId: 'macrodeck.widgets' });
    fixture.detectChanges();

    let emitted: unknown;
    component.valueChange.subscribe(v => (emitted = v));

    const picker = fixture.debugElement.query(By.directive(WidgetTargetPickerComponent))
      .componentInstance as WidgetTargetPickerComponent;
    picker.valueChange.emit('w1');

    expect(emitted).toBe('w1');
  });

});
