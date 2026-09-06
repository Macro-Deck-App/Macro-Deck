import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';

import { GetActionParameterOptionsResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import type { ActionBlockParameter } from '@macro-deck/runtime';
import { ComboboxComponent } from '../../forms/combobox/combobox.component';
import { SelectComponent } from '../../forms/select/select.component';
import { VariableTextInputComponent } from '../../forms/variable-text-input/variable-text-input.component';
import { ConditionOperandInputComponent } from './condition-operand-input.component';

type Param = Omit<ActionBlockParameter, 'value'>;

describe('ConditionOperandInputComponent', () => {
  let fixture: ComponentFixture<ConditionOperandInputComponent>;
  let apiSpy: jasmine.SpyObj<ApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getActionParameterOptions', 'onNotification']);
    apiSpy.onNotification.and.callFake(() => new Subject());

    TestBed.configureTestingModule({
      imports: [ConditionOperandInputComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(ConditionOperandInputComponent);
  });

  async function render(param: Param, value: string | number | boolean | null | undefined): Promise<void> {
    fixture.componentRef.setInput('parameter', param);
    fixture.componentRef.setInput('value', value);
    fixture.componentRef.setInput('eventId', 'obs::scene-changed');
    fixture.componentRef.setInput('currentParameters', {});
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function inputElement(): HTMLInputElement {
    return fixture.nativeElement.querySelector('.cb-input') as HTMLInputElement;
  }

  function typeInto(el: HTMLInputElement, text: string): void {
    el.value = text;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  describe('a dynamic list that cannot resolve the stored value (issue #768)', () => {
    const sceneParam: Param = { name: 'sceneName', type: 'dynamic-choice', label: 'Scene', dynamicOptions: true };

    const cases: { name: string; response: GetActionParameterOptionsResponse }[] = [
      {
        name: 'the provider is offline and answers no options at all',
        response: { options: [], allowsCustomValue: true },
      },
      {
        name: 'the provider answers options, but the scene was renamed and no longer matches',
        response: { options: [{ value: 'Outro', label: 'Outro' }, { value: 'Credits', label: 'Credits' }], allowsCustomValue: false },
      },
    ];

    for (const { name, response } of cases) {
      it(`still shows, keeps editable, and accepts typed input when ${name}`, async () => {
        apiSpy.getActionParameterOptions.and.resolveTo(response);

        await render(sceneParam, 'Intro');

        const combobox = fixture.debugElement.query(By.directive(ComboboxComponent));
        expect(combobox).withContext('a dynamic parameter renders a combobox, not a strict select').not.toBeNull();
        expect(fixture.debugElement.query(By.directive(SelectComponent))).toBeNull();

        const el = inputElement();
        expect(el.disabled).withContext('an unresolved value must stay authorable, not locked').toBeFalse();
        expect(el.value).withContext('shows the stored value even though it matched no loaded option').toBe('Intro');

        let emitted: unknown;
        fixture.componentInstance.valueChange.subscribe(v => (emitted = v));
        typeInto(el, 'Outro-typed-by-user');

        expect(emitted).toBe('Outro-typed-by-user');
      });
    }
  });

  it('renders a fixed choice as a strict select with exactly its declared options and no options request', async () => {
    const modeParam: Param = {
      name: 'mode',
      type: 'choice',
      label: 'Mode',
      options: [{ value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }],
    };

    await render(modeParam, 'a');

    const select = fixture.debugElement.query(By.directive(SelectComponent));
    expect(select).not.toBeNull();
    expect((select!.componentInstance as SelectComponent).options).toEqual([
      { value: 'a', label: 'Alpha' },
      { value: 'b', label: 'Beta' },
    ]);
    expect(fixture.debugElement.query(By.directive(ComboboxComponent))).toBeNull();
    expect(fixture.debugElement.query(By.directive(VariableTextInputComponent)))
      .withContext('no free-text fallback for a fixed choice').toBeNull();
    expect(apiSpy.getActionParameterOptions).not.toHaveBeenCalled();
  });
});
