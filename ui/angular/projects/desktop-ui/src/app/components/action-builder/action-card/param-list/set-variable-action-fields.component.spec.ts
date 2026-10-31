import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ActionBlock, AppStrings, ParameterValue, Variable } from '@macro-deck/runtime';
import { ApiService, HOST_URL_RESOLVER, InputComponent, LocalizationService } from '@shared';
import { ParamInputComponent } from '../../../forms/param-input/param-input.component';
import { SelectComponent } from '../../../forms/select/select.component';
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

  beforeEach(async () => {
    updateParam = jasmine.createSpy('updateParam');
    variables = [variable('count', 'numeric'), variable('armed', 'boolean'), variable('greeting', 'text')];

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

  it('offers a number field for a numeric variable', () => {
    render(block('count'));

    expect(selects()[0].options.map(option => option.value)).toEqual(['set', 'add']);
    expect(fixture.debugElement.query(By.directive(InputComponent)).componentInstance.type).toBe('number');
    expect(fixture.debugElement.query(By.directive(ParamInputComponent))).toBeNull();
  });

  it('names the operation and value fields after their visible labels', () => {
    render(block('count'));
    const host = fixture.nativeElement as HTMLElement;
    const trigger = host.querySelector('button[role=combobox]')!;
    const name = document.getElementById(trigger.getAttribute('aria-labelledby')!.split(' ')[0])!.textContent;

    expect(name).toContain('Operation');
    expect(host.querySelector('input[type=number]')!.getAttribute('aria-label')).toBe('Value');
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
    expect(fixture.debugElement.query(By.directive(InputComponent))).toBeNull();
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
