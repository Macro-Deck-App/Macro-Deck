import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { ApiService } from '@shared';
import type { ComparisonOperator, ConditionExpression, EventDefinition } from '@macro-deck/runtime';
import { ComboboxComponent } from '../../../forms/combobox/combobox.component';
import { VariableTextInputComponent } from '../../../forms/variable-text-input/variable-text-input.component';
import { ActionFlowStore } from '../../services/action-flow.store';
import { ConditionSectionComponent } from './condition-section.component';

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
  ],
};

const OPERATORS: ReadonlyArray<{ label: string; value: ComparisonOperator }> = [
  { label: '==', value: '==' },
  { label: '!=', value: '!=' },
];

describe('ConditionSectionComponent event payload operand (issue #768)', () => {
  let fixture: ComponentFixture<ConditionSectionComponent>;
  let apiSpy: jasmine.SpyObj<ApiService>;
  let updateBlockCondition: jasmine.Spy;
  let eventDefinitionValue: EventDefinition | undefined;

  beforeEach(() => {
    eventDefinitionValue = FOLDER_CHANGED_EVENT;

    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'getActionParameterOptions']);
    apiSpy.onNotification.and.callFake(() => new Subject());
    apiSpy.getActionParameterOptions.and.resolveTo({ options: [], allowsCustomValue: false });

    updateBlockCondition = jasmine.createSpy('updateBlockCondition');

    const store = {
      pickerVariables: () => [],
      previewScope: () => 'global' as const,
      previewScopeRefId: () => undefined,
      comparisonOperators: () => OPERATORS,
      eventDefinition: () => eventDefinitionValue,
      eventConfigurationValues: () => ({ folder: 'C:\\Videos' }),
      updateBlockCondition,
      updateBranchCondition: jasmine.createSpy('updateBranchCondition'),
      removeBranch: jasmine.createSpy('removeBranch'),
    };

    TestBed.configureTestingModule({
      imports: [ConditionSectionComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ActionFlowStore, useValue: store },
      ],
    });

    fixture = TestBed.createComponent(ConditionSectionComponent);
    fixture.componentRef.setInput('label', 'If');
    fixture.componentRef.setInput('blockId', 'block-1');
  });

  async function render(expression: ConditionExpression): Promise<void> {
    fixture.componentRef.setInput('expression', expression);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('renders the event-payload picker for an If condition, driven purely by ActionFlowStore', async () => {
    await render({ kind: 'compare', id: 'leaf-1', left: { $event: 'deviceId' }, operator: '==', right: '' });

    const combobox = fixture.debugElement.query(By.directive(ComboboxComponent));
    expect(combobox).withContext('the picker should render from store.eventDefinition() alone').not.toBeNull();

    (combobox!.componentInstance as ComboboxComponent).pick({ value: '1111-guid', label: 'MacBook Pro' });

    expect(updateBlockCondition).toHaveBeenCalledWith('block-1', jasmine.objectContaining({ right: '1111-guid' }));
  });

  it('falls back to free text when the store reports no event definition for this flow', async () => {
    eventDefinitionValue = undefined;

    await render({ kind: 'compare', id: 'leaf-1', left: { $event: 'deviceId' }, operator: '==', right: '' });

    expect(fixture.debugElement.query(By.directive(ComboboxComponent))).toBeNull();
    expect(fixture.debugElement.query(By.directive(VariableTextInputComponent))).not.toBeNull();
  });
});
