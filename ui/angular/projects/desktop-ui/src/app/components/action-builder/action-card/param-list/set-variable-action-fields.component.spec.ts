import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ActionBlock, AppStrings, ParameterValue, Variable } from '@macro-deck/runtime';
import { ApiService, HOST_URL_RESOLVER, LocalizationService } from '@shared';
import { ColorPickerComponent } from '../../../forms/color-picker/color-picker.component';
import { ParamInputComponent } from '../../../forms/param-input/param-input.component';
import { SelectComponent } from '../../../forms/select/select.component';
import { TemplateBuilderComponent } from '../../../template-builder/template-builder.component';
import { VariablePickerComponent } from '../../../variable-picker/variable-picker.component';
import { ActionFlowStore } from '../../services/action-flow.store';
import { SetVariableActionFieldsComponent } from './set-variable-action-fields.component';

describe('SetVariableActionFieldsComponent', () => {
  let fixture: ComponentFixture<SetVariableActionFieldsComponent>;
  let updateParam: jasmine.Spy;
  let variables: Variable[];

  const variable = (name: string, type: Variable['type']): Variable => ({
    id: name, name, type, scope: 'global', classification: 'user', value: '',
  });

  const block = (selected: string, operation = 'set', value: ParameterValue = ''): ActionBlock => ({
    id: 'action-1', type: 'action', blockType: 'app.macro-deck.variables.set-variable', label: 'Set Variable',
    color: '', integrationId: 'app.macro-deck.variables', actionId: 'set-variable',
    parameters: [
      { name: 'variable', type: 'autocomplete', label: 'Variable', value: selected },
      { name: 'operation', type: 'choice', label: 'Operation', value: operation },
      { name: 'value', type: 'string', label: 'Value', value },
    ],
  });

  const render = (input: ActionBlock): void => {
    fixture.componentRef.setInput('block', input);
    fixture.detectChanges();
  };

  const selects = (): SelectComponent[] =>
    fixture.debugElement.queryAll(By.directive(SelectComponent)).map(el => el.componentInstance);

  const paramInput = (): ParamInputComponent | null =>
    fixture.debugElement.query(By.directive(ParamInputComponent))?.componentInstance ?? null;

  const templateBuilder = (): TemplateBuilderComponent | null =>
    fixture.debugElement.query(By.directive(TemplateBuilderComponent))?.componentInstance ?? null;

  beforeEach(async () => {
    updateParam = jasmine.createSpy('updateParam');
    variables = [variable('count', 'numeric'), variable('armed', 'boolean'), variable('greeting', 'text'), variable('accent', 'color')];

    await TestBed.configureTestingModule({
      imports: [SetVariableActionFieldsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
        { provide: ActionFlowStore, useValue: {
          updateParam,
          variables: () => variables,
          pickerVariables: () => variables,
          previewScope: () => 'global',
          previewScopeRefId: () => undefined,
        } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SetVariableActionFieldsComponent);
  });

  it('offers a true/false list and no free text for a boolean variable', () => {
    render(block('armed'));

    expect(selects()[0].options.map(option => option.value)).toEqual(['set', 'toggle']);
    expect(selects()[1].options.map(option => option.value)).toEqual(['true', 'false']);
    expect(fixture.debugElement.query(By.directive(ParamInputComponent))).toBeNull();
  });

  it('offers a field for a plain number or a template on a numeric variable', () => {
    render(block('count', 'set', '{{ math.random 1 101 }}'));

    expect(selects()[0].options.map(option => option.value)).toEqual(['set', 'add']);
    expect(paramInput()!.value).toBe('{{ math.random 1 101 }}');
    expect(paramInput()!.acceptedVariableTypes).toBeUndefined();
    expect(paramInput()!.resultType).toBe('numeric');
  });

  it('keeps a template and a plain number as typed on a numeric variable, for Set and Add', () => {
    for (const operation of ['set', 'add']) {
      updateParam.calls.reset();
      render(block('count', operation));

      paramInput()!.valueChange.emit('{{ 1 + 1 }}');
      expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '{{ 1 + 1 }}');

      paramInput()!.valueChange.emit('-2.5');
      expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '-2.5');
    }
  });

  it('stores a decimal comma as a dot on a numeric variable, because the host reads it as a thousands separator', () => {
    render(block('count'));

    paramInput()!.valueChange.emit('1,5');

    expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '1.5');
  });

  it('leaves a comma alone in a numeric template and in a text value', () => {
    render(block('count'));
    paramInput()!.valueChange.emit('{{ [1,5] | array.size }}');
    expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '{{ [1,5] | array.size }}');

    render(block('greeting'));
    paramInput()!.valueChange.emit('1,5');
    expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '1,5');
  });

  it('keeps the true/false list as the default for a boolean variable and switches to a template from it', () => {
    render(block('armed'));

    templateBuilder()!.apply$.emit('{{ 1 > 0 }}');

    expect(paramInput()).toBeNull();
    expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '{{ 1 > 0 }}');
  });

  it('keeps the color picker as the default for a color variable and switches to a template from it', () => {
    render(block('accent', 'set', '#112233'));

    expect(fixture.debugElement.query(By.directive(ColorPickerComponent))).not.toBeNull();
    expect(paramInput()).toBeNull();

    templateBuilder()!.apply$.emit('{{ "#ff0000" }}');

    expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '{{ "#ff0000" }}');
  });

  it('opens the template editor on the literal already chosen', () => {
    render(block('armed', 'set', 'true'));

    expect(templateBuilder()!.value).toBe('true');
  });

  it('reopens a saved template in a boolean or color field as the template', () => {
    render(block('armed', 'set', '{{ 1 > 0 }}'));
    expect(paramInput()!.value).toBe('{{ 1 > 0 }}');
    expect(paramInput()!.acceptedVariableTypes).toBeUndefined();

    render(block('accent', 'set', '{{ vars.accent | color | color_lighten: 10 }}'));
    expect(paramInput()!.value).toBe('{{ vars.accent | color | color_lighten: 10 }}');
    expect(paramInput()!.acceptedVariableTypes).toBeUndefined();
    expect(fixture.debugElement.query(By.directive(ColorPickerComponent))).toBeNull();
  });

  it('shows a value left over from another variable type instead of an empty control', () => {
    render(block('armed', 'set', '5'));

    expect(paramInput()!.value).toBe('5');
  });

  it('returns to the fixed control when a template is cleared', () => {
    render(block('armed', 'set', '{{ 1 > 0 }}'));
    render(block('armed', 'set', ''));

    expect(paramInput()).toBeNull();
    expect(selects()).toHaveSize(2);
  });

  it('keeps the template field while a color is typed through values that are already valid', () => {
    render(block('accent', 'set', '{{ vars.accent }}'));

    paramInput()!.valueChange.emit('#ff0');
    render(block('accent', 'set', '#ff0'));

    expect(paramInput()!.value).toBe('#ff0');
    expect(fixture.debugElement.query(By.directive(ColorPickerComponent))).toBeNull();
  });

  it('keeps an emptied field while it is being edited and returns to the fixed control when it is left', () => {
    render(block('armed', 'set', '{{ 1 > 0 }}'));

    paramInput()!.valueChange.emit('');
    render(block('armed', 'set', ''));
    expect(paramInput()).not.toBeNull();

    paramInput()!.valueBlur.emit('');
    fixture.detectChanges();
    expect(paramInput()).toBeNull();
  });

  it('shows the fixed control for a literal after the target changes to a boolean variable', () => {
    render(block('greeting', 'set', 'true'));
    paramInput()!.valueChange.emit('true');

    render(block('armed', 'set', 'true'));

    expect(paramInput()).toBeNull();
  });

  it('keeps a field that is left with a value in it', () => {
    render(block('armed', 'set', '{{ 1 > 0 }}'));

    paramInput()!.valueBlur.emit('{{ 1 > 0 }}');
    fixture.detectChanges();

    expect(paramInput()).not.toBeNull();
  });

  it('returns to the fixed control after a picked reference is cleared', () => {
    render(block('armed', 'set', '{{ 1 > 0 }}'));
    paramInput()!.valueChange.emit('{{ 0 > 1 }}');

    fixture.debugElement.query(By.directive(VariablePickerComponent)).componentInstance.pick.emit('other');
    render(block('armed', 'set', ''));

    expect(paramInput()).toBeNull();
  });

  it('treats a field emptied down to a line break as cleared for a boolean, color or number', () => {
    render(block('armed', 'set', '{{ 1 > 0 }}'));

    paramInput()!.valueChange.emit('\n');
    render(block('armed', 'set', ''));
    paramInput()!.valueBlur.emit('\n');
    fixture.detectChanges();

    expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '');
    expect(paramInput()).toBeNull();
  });

  it('keeps a line break as typed in a text value', () => {
    render(block('greeting'));

    paramInput()!.valueChange.emit('\n');

    expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '\n');
  });

  it('reopens a saved literal in the fixed control', () => {
    render(block('armed', 'set', 'true'));

    expect(paramInput()).toBeNull();
  });

  it('shows a stored JSON boolean in the true/false list', () => {
    render(block('armed', 'set', true));

    expect(paramInput()).toBeNull();
    expect(templateBuilder()!.value).toBe('true');
  });

  it('names the operation and value fields after their visible labels', () => {
    render(block('armed'));
    const host = fixture.nativeElement as HTMLElement;
    const trigger = host.querySelector('button[role=combobox]')!;
    const name = document.getElementById(trigger.getAttribute('aria-labelledby')!.split(' ')[0])!.textContent;

    expect(name).toContain('Operation');
    const value = host.querySelectorAll('button[role=combobox]')[1];
    expect(document.getElementById(value.getAttribute('aria-labelledby')!.split(' ')[0])!.textContent).toContain('Value');

    render(block('count'));
    expect(host.querySelector('[role=textbox]')!.getAttribute('aria-label')).toBe('Value');
  });

  it('names the text value field, whichever editor it renders', () => {
    render(block('greeting'));

    expect((fixture.nativeElement as HTMLElement).querySelector('[role=textbox]')!.getAttribute('aria-label'))
      .toBe('Value');
  });

  it('offers a text field for a text variable', () => {
    render(block('greeting'));

    expect(selects()[0].options.map(option => option.value)).toEqual(['set', 'append']);
    expect(fixture.debugElement.query(By.directive(ParamInputComponent))).not.toBeNull();
  });

  it('keeps every operation and a text field while the variable is unknown', () => {
    render(block('typed_by_hand'));

    expect(selects()[0].options.map(option => option.value)).toEqual(['set', 'add', 'toggle', 'append']);
    expect(fixture.debugElement.query(By.directive(ParamInputComponent))).not.toBeNull();
  });

  it('hides the value field for Toggle, which reads the current one', () => {
    render(block('armed', 'toggle'));

    expect(selects()).toHaveSize(1);
  });

  it('falls back to Set when the picked type cannot perform the stored operation', () => {
    render(block('count', 'append'));

    expect(updateParam).toHaveBeenCalledWith('action-1', 'operation', 'set');
  });

  it('leaves a valid stored operation alone', () => {
    render(block('count', 'add'));

    expect(updateParam).not.toHaveBeenCalled();
  });

  it('offers a variable picker restricted to the matching type for a scalar', () => {
    render(block('count'));

    const picker = fixture.debugElement.query(By.directive(VariablePickerComponent));
    expect(picker.componentInstance.acceptedTypes).toEqual(['numeric']);
  });

  it('shows a picked reference as a chip instead of an empty field', () => {
    render(block('count', 'add', { $var: 'other' }));

    expect(fixture.nativeElement.querySelector('.chip-text').textContent.trim()).toBe('{{ vars.other }}');
    expect(paramInput()).toBeNull();
  });

  it('clears a reference back to an editable value', () => {
    render(block('count', 'add', { $var: 'other' }));

    fixture.nativeElement.querySelector('.chip-clear').click();

    expect(updateParam).toHaveBeenCalledWith('action-1', 'value', '');
  });
});

describe('SetVariableActionFieldsComponent labels', () => {
  const actions = AppStrings.Integrations.Variables.Actions;
  const german = {
    [actions.OperationSetTo]: 'Setzen auf',
    [actions.OperationAdd]: 'Addieren',
    [actions.OperationToggle]: 'Umschalten',
    [actions.OperationAppend]: 'Anhängen',
  };
  const french = { ...german, [actions.OperationToggle]: 'Basculer' };

  let getLocalization: jasmine.Spy;

  beforeEach(async () => {
    localStorage.clear();
    getLocalization = jasmine.createSpy('getLocalization')
      .and.resolveTo({ culture: 'de', fallbackCulture: 'en', translations: german });

    await TestBed.configureTestingModule({
      imports: [SetVariableActionFieldsComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: {
          getLocalization,
          onNotification: () => new Subject(),
          connectionStateSignal: signal('disconnected'),
        } },
        { provide: ActionFlowStore, useValue: {
          updateParam: () => undefined,
          variables: () => [],
          pickerVariables: () => [],
          previewScope: () => 'global',
          previewScopeRefId: () => undefined,
        } },
      ],
    }).compileComponents();
  });

  afterEach(() => localStorage.clear());

  it('shows every operation in the language of the catalog and follows a change of it', async () => {
    const localization = TestBed.inject(LocalizationService);
    await localization.loadFromHost();
    const fixture = TestBed.createComponent(SetVariableActionFieldsComponent);
    fixture.componentRef.setInput('block', {
      id: 'action-1', type: 'action', blockType: 'app.macro-deck.variables.set-variable', label: 'Set Variable',
      color: '', parameters: [{ name: 'operation', type: 'choice', label: 'Operation', value: 'toggle' }],
    } satisfies ActionBlock);
    await fixture.whenStable();
    const labels = (): string[] => fixture.debugElement.query(By.directive(SelectComponent)).componentInstance.options
      .map((option: { label: string }) => option.label);

    expect(labels()).toEqual(['Setzen auf', 'Addieren', 'Umschalten', 'Anhängen']);

    getLocalization.and.resolveTo({ culture: 'fr', fallbackCulture: 'en', translations: french });
    await localization.loadFromHost();
    await fixture.whenStable();

    expect(labels()).toEqual(['Setzen auf', 'Addieren', 'Basculer', 'Anhängen']);
  });
});
