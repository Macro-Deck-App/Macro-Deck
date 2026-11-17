import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import type { GetActionParameterOptionsRequest, Variable } from '@macro-deck/runtime';
import { ActionOptionsService } from '../../services/action-options.service';
import { VariableCreateDialogComponent } from './variable-create-dialog.component';

describe('VariableCreateDialogComponent', () => {
  let fixture: ComponentFixture<VariableCreateDialogComponent>;
  let component: VariableCreateDialogComponent;
  let api: jasmine.SpyObj<ApiService>;
  let created: Variable[];
  let closedCount: number;

  const score: Variable = {
    id: 'v-score',
    name: 'score',
    scope: 'global',
    type: 'numeric',
    classification: 'user',
    value: '5',
  };

  const userVariablesRequest: GetActionParameterOptionsRequest = {
    integrationId: 'app.macro-deck.system',
    actionId: 'ask-question',
    parameterName: 'answerVariable',
    optionsSourceId: 'macrodeck.user-variables',
    filter: 'score',
  };

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables',
      'createVariable',
      'getActionParameterOptions',
      'onNotification',
    ]);
    api.getVariables.and.resolveTo({ variables: [] });
    api.createVariable.and.resolveTo({ success: true, variable: score });
    api.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [VariableCreateDialogComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
      ],
    });

    fixture = TestBed.createComponent(VariableCreateDialogComponent);
    component = fixture.componentInstance;
    created = [];
    closedCount = 0;
    component.created.subscribe(variable => created.push(variable));
    component.closed.subscribe(() => closedCount++);
  });

  async function render(inputs: Record<string, unknown> = {}): Promise<void> {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function closeAnimation(): Promise<void> {
    await new Promise(resolve => setTimeout(resolve, 200));
  }

  function nameField(): HTMLInputElement {
    return fixture.nativeElement.querySelector('shared-input[name="name"] input');
  }

  it('starts with the name typed in the field it was opened from', async () => {
    await render({ initialName: 'score' });

    expect(nameField().value).toBe('score');
    expect(fixture.nativeElement.querySelector('.form-preview')?.textContent).toContain('vars.score');
    expect(component.canSubmitCreate()).toBeTrue();
  });

  it('starts on the scope of the editor it was opened from', async () => {
    await render({ initialName: 'score', scope: 'widget', scopeRefId: 'w1' });

    await component.submitCreate();

    expect(api.createVariable).toHaveBeenCalledWith(jasmine.objectContaining({
      name: 'score',
      scope: 'widget',
      scopeRefId: 'w1',
    }));
  });

  it('hands the created variable back and closes', async () => {
    await render({ initialName: 'score' });
    component.setFormType('numeric');
    component.setFormInitialValue('5');

    await component.submitCreate();
    await closeAnimation();

    expect(api.createVariable).toHaveBeenCalledWith(jasmine.objectContaining({
      name: 'score',
      type: 'numeric',
      initialValue: '5',
    }));
    expect(created).toEqual([score]);
    expect(closedCount).toBe(1);
  });

  it('stays open with the reason when the host refuses the variable', async () => {
    api.createVariable.and.resolveTo({ success: false, error: { code: 'NameTaken', message: '' } } as never);
    await render({ initialName: 'score' });

    await component.submitCreate();

    expect(created).toEqual([]);
    expect(closedCount).toBe(0);
    expect(component.createError()).toBeTruthy();
  });

  it('closes without creating anything on cancel', async () => {
    await render({ initialName: 'score' });

    component.close();
    await closeAnimation();

    expect(api.createVariable).not.toHaveBeenCalled();
    expect(created).toEqual([]);
    expect(closedCount).toBe(1);
  });

  it('makes user-variable suggestions answered before the create ask the host again', async () => {
    const options = TestBed.inject(ActionOptionsService);
    api.getActionParameterOptions.and.resolveTo({ options: [], allowsCustomValue: true, cacheSeconds: 5 });
    await options.getOptions(userVariablesRequest);
    api.getActionParameterOptions.and.resolveTo(
      { options: [{ value: 'score' }], allowsCustomValue: true, cacheSeconds: 5 });
    await render({ initialName: 'score' });

    await component.submitCreate();
    const after = await options.getOptions(userVariablesRequest);

    expect(after.options.map(o => o.value)).toEqual(['score']);
  });
});
