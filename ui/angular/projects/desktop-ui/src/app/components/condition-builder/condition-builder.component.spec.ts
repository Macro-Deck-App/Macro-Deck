import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import type { ComparisonExpression, ComparisonOperator, ConditionExpression, EventDefinition, Variable } from '@macro-deck/runtime';
import { ComboboxComponent } from '../forms/combobox/combobox.component';
import { SelectComponent } from '../forms/select/select.component';
import { VariableTextInputComponent } from '../forms/variable-text-input/variable-text-input.component';
import { ConditionBuilderComponent } from './condition-builder.component';

function variable(overrides: Partial<Variable>): Variable {
  return {
    id: 'id',
    name: 'name',
    scope: 'global',
    type: 'text',
    classification: 'user',
    value: '',
    ...overrides,
  };
}

function compare(overrides: Partial<ComparisonExpression>): ComparisonExpression {
  return {
    kind: 'compare',
    id: 'leaf-1',
    left: '',
    operator: '==',
    right: '',
    ...overrides,
  };
}

describe('ConditionBuilderComponent', () => {
  let fixture: ComponentFixture<ConditionBuilderComponent>;
  let component: ConditionBuilderComponent;

  const boolVar = variable({ id: 'b1', name: 'is_playing', type: 'boolean', value: 'false' });
  const textVar = variable({ id: 't1', name: 'title', type: 'text', value: 'song' });

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables',
      'getIntegrations',
      'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [ConditionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(ConditionBuilderComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('variables', [boolVar, textVar]);
  });

  async function render(expression: ConditionExpression): Promise<void> {
    fixture.componentRef.setInput('expression', expression);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  describe('isBooleanOperand', () => {
    it('flags the literal side when the opposite operand is a boolean variable', () => {
      const comp = compare({ left: { $var: 'is_playing' }, right: '' });
      expect(component.isBooleanOperand(comp, 'right')).toBeTrue();
    });

    it('is symmetric - a boolean variable on the right makes the left a boolean operand', () => {
      const comp = compare({ left: '', right: { $var: 'is_playing' } });
      expect(component.isBooleanOperand(comp, 'left')).toBeTrue();
    });

    it('does not flag when the opposite operand is a non-boolean variable', () => {
      const comp = compare({ left: { $var: 'title' }, right: '' });
      expect(component.isBooleanOperand(comp, 'right')).toBeFalse();
    });

    it('does not flag when the opposite operand is a literal', () => {
      const comp = compare({ left: 'foo', right: '' });
      expect(component.isBooleanOperand(comp, 'right')).toBeFalse();
    });

    it('does not flag an unknown variable name', () => {
      const comp = compare({ left: { $var: 'not_a_var' }, right: '' });
      expect(component.isBooleanOperand(comp, 'right')).toBeFalse();
    });
  });

  describe('boolean operand display value', () => {
    it('selects true/false for the recognised stored forms, and shows the placeholder for anything else', async () => {
      const cases: { stored: boolean | string | null; expectedSelection: string | null }[] = [
        { stored: true, expectedSelection: 'true' },
        { stored: 'true', expectedSelection: 'true' },
        { stored: false, expectedSelection: 'false' },
        { stored: 'false', expectedSelection: 'false' },
        { stored: '', expectedSelection: null },
        { stored: 'yes', expectedSelection: null },
        { stored: null, expectedSelection: null },
      ];

      for (const { stored, expectedSelection } of cases) {
        await render(compare({ left: { $var: 'is_playing' }, right: stored }));

        const select = fixture.debugElement.query(By.directive(SelectComponent)).componentInstance as SelectComponent;
        expect(select.value).withContext(`stored value ${JSON.stringify(stored)}`).toBe(expectedSelection);
      }
    });
  });

  describe('rendering', () => {
    it('renders a true/false dropdown for the value when the other side is a boolean variable', async () => {
      await render(compare({ left: { $var: 'is_playing' }, right: '' }));

      const select = fixture.debugElement.query(By.directive(SelectComponent));
      expect(select).withContext('boolean operand should render shared-select').not.toBeNull();
      expect(fixture.nativeElement.querySelector('.cb-literal-wrap-control .cb-literal-input'))
        .withContext('no free-text input for a boolean value')
        .toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.cb-literal-input').length).toBe(1);

      const options = (select.componentInstance as SelectComponent).options.map(o => o.value);
      expect(options).toEqual(['true', 'false']);
    });

    it('filters the variable picker to boolean variables in the boolean branch', async () => {
      await render(compare({ left: { $var: 'is_playing' }, right: '' }));

      const wrap = fixture.nativeElement.querySelector('.cb-literal-wrap-control');
      expect(wrap).not.toBeNull();
      const picker = fixture.debugElement.query(By.css('.cb-literal-wrap-control shared-variable-picker'));
      expect(picker.componentInstance.acceptedTypes).toEqual(['boolean']);
    });

    it('keeps a free-text input when neither side is a boolean variable', async () => {
      await render(compare({ left: 'foo', right: 'bar' }));

      expect(fixture.debugElement.query(By.directive(SelectComponent)))
        .withContext('no dropdown for plain literals')
        .toBeNull();
      expect(fixture.nativeElement.querySelectorAll('.cb-literal-input').length).toBe(2);
    });

    it('stores the chosen value as the "true"/"false" string the host compares against', async () => {
      await render(compare({ left: { $var: 'is_playing' }, right: '' }));

      let emitted: ConditionExpression | undefined;
      component.expressionChange.subscribe(e => (emitted = e));

      const select = fixture.debugElement.query(By.directive(SelectComponent))
        .componentInstance as SelectComponent;
      select.pick({ value: 'true', label: 'true' });

      expect(emitted).toBeDefined();
      const right = (emitted as ComparisonExpression).right as string;
      expect(typeof right).toBe('string');
      expect(right).toBe('true');
    });
  });
});

describe('ConditionBuilderComponent state operators', () => {
  let fixture: ComponentFixture<ConditionBuilderComponent>;
  let component: ConditionBuilderComponent;

  const FULL_OPERATORS: ReadonlyArray<{ label: string; value: ComparisonOperator }> = [
    { label: '==', value: '==' },
    { label: '!=', value: '!=' },
    { label: '>', value: '>' },
    { label: '<', value: '<' },
    { label: '>=', value: '>=' },
    { label: '<=', value: '<=' },
    { label: 'contains', value: 'contains' },
    { label: 'startsWith', value: 'startsWith' },
    { label: 'isEmpty', value: 'isEmpty' },
    { label: 'isNotEmpty', value: 'isNotEmpty' },
    { label: 'isAvailable', value: 'isAvailable' },
    { label: 'isNotAvailable', value: 'isNotAvailable' },
  ];

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables',
      'getIntegrations',
      'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [ConditionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(ConditionBuilderComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('variables', []);
    fixture.componentRef.setInput('comparisonOperators', FULL_OPERATORS);
  });

  async function render(expression: ConditionExpression): Promise<void> {
    fixture.componentRef.setInput('expression', expression);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('D6: renders exactly one operand and no right-hand input for a state operator, and restores the stored right value on switching back', async () => {
    const stored = compare({ left: { $var: 'title' }, operator: 'isNotEmpty', right: '' });
    await render(stored);

    expect(fixture.debugElement.queryAll(By.css('.cb-operand')).length).toBe(1);

    let emitted: ConditionExpression | undefined;
    component.expressionChange.subscribe(e => (emitted = e));

    const withRight = compare({ left: { $var: 'title' }, operator: '==', right: 'kept-value' });
    await render(withRight);
    component.pickOperator(withRight, 'isNotEmpty', e => component.expressionChange.emit(e));
    expect(emitted).toBeDefined();
    // The right operand is preserved though hidden - an accidental operator change must not be
    // destructive, and the host ignores it anyway for a state operator.
    const emittedRight = (emitted as ComparisonExpression).right as string;
    expect(emittedRight).toBe('kept-value');
    expect((emitted as ComparisonExpression).operator).toBe('isNotEmpty');

    // Switching back to '==' must restore what was typed.
    const backToEquality = { ...(emitted as ComparisonExpression), operator: '==' as ComparisonOperator };
    await render(backToEquality);
    expect(fixture.debugElement.queryAll(By.css('.cb-operand')).length).toBe(2);
    const rightOperand = fixture.debugElement.queryAll(By.css('.cb-operand'))[1];
    const rightInput = rightOperand.query(By.directive(VariableTextInputComponent));
    expect(rightInput.componentInstance.value).toBe('kept-value');
  });
});

describe('ConditionBuilderComponent inline references', () => {
  let fixture: ComponentFixture<ConditionBuilderComponent>;
  let component: ConditionBuilderComponent;

  const flag = variable({ id: 'b9', name: 'flag', type: 'boolean' });

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ConditionBuilderComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: { onNotification: () => new Subject() } },
      ],
    });
    fixture = TestBed.createComponent(ConditionBuilderComponent);
    component = fixture.componentInstance;
    component.variables = [flag];
  });

  function render(expression: ConditionExpression): void {
    fixture.componentRef.setInput('expression', expression);
    fixture.detectChanges();
  }

  it('offers a picker on both operands in every state', () => {
    const states: ConditionExpression[] = [
      compare({ left: '', right: '' }),
      compare({ left: '{{ vars.other }}', right: 'text' }),
      compare({ left: { $var: 'flag' }, right: '' }),
      compare({ left: { $var: 'flag' }, right: { $var: 'flag' } }),
    ];

    for (const state of states) {
      render(state);
      expect(fixture.debugElement.queryAll(By.css('shared-variable-picker')).length)
        .withContext(JSON.stringify(state))
        .toBe(2);
    }
  });

  it('keeps both operands changeable when two booleans are compared', () => {
    render(compare({ left: { $var: 'flag' }, right: { $var: 'flag' } }));

    expect(fixture.nativeElement.querySelectorAll('.cb-chip').length).toBe(2);
    expect(fixture.debugElement.queryAll(By.css('.cb-literal-wrap-control shared-variable-picker')).length)
      .toBe(2);
  });

  it('renders a stored reference inline rather than as a whole-operand chip', () => {
    render(compare({ left: { $var: 'other' }, right: '' }));

    expect(fixture.debugElement.queryAll(By.css('.cb-chip')).length).toBe(0);
    expect(component.literalText({ $var: 'other' })).toBe('{{ vars.other }}');
    expect(component.literalText({ $event: 'payload' })).toBe('{{ event.payload }}');
    expect(component.literalText('{{ vars.other }}')).toBe('{{ vars.other }}');
    expect(component.literalText('prefix {{ vars.other }}')).toBe('prefix {{ vars.other }}');
    expect(component.asPrimitive('{{ vars.other }}')).toBe('{{ vars.other }}');
  });

  it('keeps the boolean dropdown when the opposite operand is a typed reference', () => {
    // Issue #97: the dropdown keeps the stored literal valid. It used to key off the object form
    // only, so it vanished the moment the reference was typed instead of picked.
    render(compare({ left: '{{ vars.flag }}', right: '' }));

    expect(fixture.debugElement.queryAll(By.css('shared-select')).length).toBe(1);
  });

  it('renders the dropdown branch with its picker when the opposite operand is boolean', () => {
    render(compare({ left: { $var: 'flag' }, right: '' }));

    expect(fixture.debugElement.queryAll(By.css('.cb-literal-wrap-control shared-variable-picker')).length)
      .toBe(1);
  });

  it('labels a typed reference the same as a stored one', () => {
    expect(component.variableLabel('{{ vars.flag }}')).toBe('vars.flag');
    expect(component.variableLabel({ $var: 'flag' })).toBe('vars.flag');
    expect(component.variableLabel('some text')).toBe('');
  });
});

const ALL_OPERATORS: ReadonlyArray<{ label: string; value: ComparisonOperator }> = [
  { label: '==', value: '==' },
  { label: '!=', value: '!=' },
  { label: '>', value: '>' },
  { label: '<', value: '<' },
  { label: '>=', value: '>=' },
  { label: '<=', value: '<=' },
  { label: 'contains', value: 'contains' },
  { label: 'startsWith', value: 'startsWith' },
  { label: 'isEmpty', value: 'isEmpty' },
  { label: 'isNotEmpty', value: 'isNotEmpty' },
  { label: 'isAvailable', value: 'isAvailable' },
  { label: 'isNotAvailable', value: 'isNotAvailable' },
];

const FOLDER_CHANGED_EVENT: EventDefinition = {
  id: 'macro-deck::folder-changed',
  providerId: 'macro-deck',
  providerName: 'Macro Deck',
  isIntegration: false,
  name: 'Folder Changed',
  deliveryKind: 'push',
  configurationParameters: [],
  payloadParameters: [
    { name: 'deviceId', type: 'dynamic-choice', label: 'Device', dynamicOptions: true, optionsSourceId: 'macrodeck.devices' },
    { name: 'level', type: 'number', label: 'Level' },
    { name: 'note', type: 'string', label: 'Note' },
    { name: 'isEmpty', type: 'boolean', label: 'Is Empty' },
    { name: 'mode', type: 'choice', label: 'Mode', options: [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }] },
  ],
};

describe('ConditionBuilderComponent event payload operand (issue #768)', () => {
  let fixture: ComponentFixture<ConditionBuilderComponent>;
  let component: ConditionBuilderComponent;
  let apiSpy: jasmine.SpyObj<ApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables',
      'getIntegrations',
      'onNotification',
      'getActionParameterOptions',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());
    apiSpy.getActionParameterOptions.and.resolveTo({ options: [], allowsCustomValue: false });

    TestBed.configureTestingModule({
      imports: [ConditionBuilderComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(ConditionBuilderComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('variables', []);
  });

  async function render(expression: ConditionExpression): Promise<void> {
    fixture.componentRef.setInput('expression', expression);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function operandAt(index: 0 | 1) {
    return fixture.debugElement.queryAll(By.css('.cb-operand'))[index];
  }

  function operatorLabels(comp: ComparisonExpression): string[] {
    component.toggleOperatorPopover(comp, new Event('click'));
    fixture.detectChanges();
    const labels = Array.from(fixture.nativeElement.querySelectorAll('.cb-operator-option'))
      .map(el => (el as HTMLElement).textContent!.trim());
    component.closeOperatorPopover();
    fixture.detectChanges();
    return labels;
  }

  it('renders the picker only on the operand opposite the event reference', async () => {
    fixture.componentRef.setInput('eventDefinition', FOLDER_CHANGED_EVENT);
    await render(compare({ left: { $event: 'deviceId' }, operator: '==', right: '' }));

    const [leftOperand, rightOperand] = fixture.debugElement.queryAll(By.css('.cb-operand'));
    expect(rightOperand.query(By.directive(ComboboxComponent)))
      .withContext('right (the empty side) should render the picker').not.toBeNull();

    // An implementation that turns both sides into pickers must fail here: the left keeps showing
    // the event.deviceId reference it already held, never a second combobox.
    expect(leftOperand.query(By.directive(ComboboxComponent)))
      .withContext('left must not also become a picker').toBeNull();
    expect(leftOperand.query(By.directive(SelectComponent))).toBeNull();
    const leftText = leftOperand.query(By.directive(VariableTextInputComponent));
    expect(leftText).withContext('left keeps showing the event.deviceId reference').not.toBeNull();
    expect((leftText!.componentInstance as VariableTextInputComponent).value).toBe('{{ event.deviceId }}');
  });

  it('is symmetric: the picker follows whichever side is empty', async () => {
    fixture.componentRef.setInput('eventDefinition', FOLDER_CHANGED_EVENT);
    await render(compare({ left: '', operator: '==', right: { $event: 'deviceId' } }));

    const [leftOperand, rightOperand] = fixture.debugElement.queryAll(By.css('.cb-operand'));
    expect(leftOperand.query(By.directive(ComboboxComponent)))
      .withContext('left (the empty side) should render the picker').not.toBeNull();
    expect(rightOperand.query(By.directive(ComboboxComponent)))
      .withContext('right must not also become a picker').toBeNull();
    const rightText = rightOperand.query(By.directive(VariableTextInputComponent));
    expect((rightText!.componentInstance as VariableTextInputComponent).value).toBe('{{ event.deviceId }}');
  });

  it('treats the stored object reference and the lone typed token identically', async () => {
    fixture.componentRef.setInput('eventDefinition', FOLDER_CHANGED_EVENT);
    fixture.componentRef.setInput('comparisonOperators', ALL_OPERATORS);

    const stateOps: ComparisonOperator[] = ['isEmpty', 'isNotEmpty', 'isAvailable', 'isNotAvailable'];

    const objectForm = compare({ left: { $event: 'deviceId' }, operator: '==', right: '' });
    await render(objectForm);
    expect(operandAt(1).query(By.directive(ComboboxComponent))).withContext('object form').not.toBeNull();
    expect(operatorLabels(objectForm)).toEqual(['==', '!=', ...stateOps]);

    const tokenForm = compare({ left: '{{ event.deviceId }}', operator: '==', right: '' });
    await render(tokenForm);
    expect(operandAt(1).query(By.directive(ComboboxComponent))).withContext('token form').not.toBeNull();
    expect(operatorLabels(tokenForm)).toEqual(['==', '!=', ...stateOps]);
  });

  it('does not treat a variable token or a token mixed with other text as an event reference', async () => {
    fixture.componentRef.setInput('eventDefinition', FOLDER_CHANGED_EVENT);

    for (const notAToken of ['{{ vars.deviceId }}', 'prefix {{ event.deviceId }} suffix']) {
      await render(compare({ left: notAToken, operator: '==', right: '' }));
      const rightOperand = operandAt(1);
      expect(rightOperand.query(By.directive(ComboboxComponent))).withContext(notAToken).toBeNull();
      expect(rightOperand.query(By.directive(SelectComponent))).withContext(notAToken).toBeNull();
      expect(rightOperand.query(By.directive(VariableTextInputComponent))).withContext(notAToken).not.toBeNull();
    }
  });

  it('stores the picked value, not its label, in the emitted expression', async () => {
    apiSpy.getActionParameterOptions.and.resolveTo({
      options: [{ value: '1111-guid', label: 'MacBook Pro' }],
      allowsCustomValue: false,
    });
    fixture.componentRef.setInput('eventDefinition', FOLDER_CHANGED_EVENT);
    const comp = compare({ left: { $event: 'deviceId' }, operator: '==', right: '' });
    await render(comp);

    let emitted: ConditionExpression | undefined;
    component.expressionChange.subscribe(e => (emitted = e));

    const combobox = operandAt(1).query(By.directive(ComboboxComponent)).componentInstance as ComboboxComponent;
    combobox.open();
    await fixture.whenStable();
    // "MacBook Pro" is what the user sees and picks; the id is what must be stored.
    combobox.pick({ value: '1111-guid', label: 'MacBook Pro' });

    expect(emitted).toBeDefined();
    const right = (emitted as ComparisonExpression).right as string;
    expect(right).toBe('1111-guid');
  });

  it('requests options with the event context, not an action context', async () => {
    fixture.componentRef.setInput('eventDefinition', FOLDER_CHANGED_EVENT);
    fixture.componentRef.setInput('eventConfiguration', { folder: 'C:\\Videos' });
    await render(compare({ left: { $event: 'deviceId' }, operator: '==', right: '' }));

    const combobox = operandAt(1).query(By.directive(ComboboxComponent)).componentInstance as ComboboxComponent;
    combobox.open();
    await fixture.whenStable();

    expect(apiSpy.getActionParameterOptions).toHaveBeenCalledTimes(1);
    const request = apiSpy.getActionParameterOptions.calls.mostRecent().args[0];
    expect(request.eventId).toBe('macro-deck::folder-changed');
    expect(request.eventParameterKind).toBe('payload');
    expect(request.parameterName).toBe('deviceId');
    expect(request.currentParameters).toEqual({ folder: 'C:\\Videos' });
    expect(request.integrationId).toBe('');
    expect(request.actionId).toBe('');
  });

  it('narrows the operator list to what the resolved operand type supports', async () => {
    fixture.componentRef.setInput('eventDefinition', FOLDER_CHANGED_EVENT);
    fixture.componentRef.setInput('comparisonOperators', ALL_OPERATORS);

    // The four state operators (isEmpty/isNotEmpty/isAvailable/isNotAvailable) survive every
    // narrowing: "did this arrive at all" is a question every operand type can be asked, so an
    // author who picks one on a choice/boolean/numeric row must not see it vanish from its own list.
    const STATE_OPS: ComparisonOperator[] = ['isEmpty', 'isNotEmpty', 'isAvailable', 'isNotAvailable'];
    const cases: { paramName: string; expected: ComparisonOperator[] }[] = [
      { paramName: 'deviceId', expected: ['==', '!=', ...STATE_OPS] },
      { paramName: 'mode', expected: ['==', '!=', ...STATE_OPS] },
      { paramName: 'isEmpty', expected: ['==', '!=', ...STATE_OPS] },
      { paramName: 'level', expected: ['==', '!=', '>', '<', '>=', '<=', ...STATE_OPS] },
      { paramName: 'note', expected: ALL_OPERATORS.map(o => o.value) },
    ];

    for (const { paramName, expected } of cases) {
      const comp = compare({ left: { $event: paramName }, operator: '==', right: '' });
      await render(comp);
      expect(operatorLabels(comp)).withContext(paramName).toEqual(expected);
    }
  });

  it('degrades to free text with the full operator list, and keeps the stored value untouched, when nothing resolves', async () => {
    fixture.componentRef.setInput('comparisonOperators', ALL_OPERATORS);

    // (a) the referenced name is not among the event's payload parameters.
    fixture.componentRef.setInput('eventDefinition', FOLDER_CHANGED_EVENT);
    let comp = compare({ left: { $event: 'unknownField' }, operator: '==', right: 'kept-a' });
    await render(comp);
    let rightOperand = operandAt(1);
    expect(rightOperand.query(By.directive(ComboboxComponent))).withContext('unknown name').toBeNull();
    expect((rightOperand.query(By.directive(VariableTextInputComponent))!.componentInstance as VariableTextInputComponent).value)
      .toBe('kept-a');
    expect(operatorLabels(comp)).toEqual(ALL_OPERATORS.map(o => o.value));

    // (b) the payload parameter is declared plain 'string'.
    comp = compare({ left: { $event: 'note' }, operator: '==', right: 'kept-b' });
    await render(comp);
    rightOperand = operandAt(1);
    expect(rightOperand.query(By.directive(ComboboxComponent))).withContext('string-typed payload param').toBeNull();
    expect((rightOperand.query(By.directive(VariableTextInputComponent))!.componentInstance as VariableTextInputComponent).value)
      .toBe('kept-b');
    expect(operatorLabels(comp)).toEqual(ALL_OPERATORS.map(o => o.value));

    // ...and a reference on that side stays an editable inline token rather than becoming a chip that
    // can only be cleared, which is what the typed-control branch renders.
    comp = compare({ left: { $event: 'note' }, operator: '==', right: '{{ vars.title }}' });
    await render(comp);
    rightOperand = operandAt(1);
    expect(rightOperand.query(By.css('.cb-chip'))).withContext('a text payload param must not chip the value').toBeNull();
    expect((rightOperand.query(By.directive(VariableTextInputComponent))!.componentInstance as VariableTextInputComponent).value)
      .toBe('{{ vars.title }}');

    // (c) no event definition supplied at all.
    fixture.componentRef.setInput('eventDefinition', undefined);
    comp = compare({ left: { $event: 'deviceId' }, operator: '==', right: 'kept-c' });
    await render(comp);
    rightOperand = operandAt(1);
    expect(rightOperand.query(By.directive(ComboboxComponent))).withContext('no event definition').toBeNull();
    expect((rightOperand.query(By.directive(VariableTextInputComponent))!.componentInstance as VariableTextInputComponent).value)
      .toBe('kept-c');
    expect(operatorLabels(comp)).toEqual(ALL_OPERATORS.map(o => o.value));
  });

  it('leaves plain literals, the full operator list and the boolean-variable dropdown unchanged with no event definition', async () => {
    const boolVar = variable({ id: 'b1', name: 'is_playing', type: 'boolean', value: 'false' });
    fixture.componentRef.setInput('variables', [boolVar]);
    fixture.componentRef.setInput('comparisonOperators', ALL_OPERATORS);

    let comp = compare({ left: 'foo', operator: '==', right: 'bar' });
    await render(comp);
    expect(fixture.debugElement.queryAll(By.directive(VariableTextInputComponent)).length).toBe(2);
    expect(operatorLabels(comp)).toEqual(ALL_OPERATORS.map(o => o.value));

    comp = compare({ left: { $var: 'is_playing' }, operator: '==', right: '' });
    await render(comp);
    expect(operandAt(1).query(By.directive(SelectComponent)))
      .withContext('boolean-variable dropdown must still appear with no eventDefinition set').not.toBeNull();
    expect(operatorLabels(comp))
      .withContext('narrowing follows a declared payload parameter, never a variable, so this list is untouched')
      .toEqual(ALL_OPERATORS.map(o => o.value));
  });
});
