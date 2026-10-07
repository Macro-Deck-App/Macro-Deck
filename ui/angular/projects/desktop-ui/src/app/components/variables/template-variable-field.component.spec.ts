import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import { ParamInputComponent } from '../forms/param-input/param-input.component';
import { TemplateVariableFieldComponent } from './template-variable-field.component';

describe('TemplateVariableFieldComponent', () => {
  let fixture: ComponentFixture<TemplateVariableFieldComponent>;

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getVariables', 'getIntegrations', 'onNotification']);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [TemplateVariableFieldComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(TemplateVariableFieldComponent);
    fixture.componentRef.setInput('value', 'CPU {{ vars.cpu }}');
    fixture.componentRef.setInput('resultType', 'numeric');
    fixture.componentRef.setInput('resultOptions', { decimalPlaces: 2 });
  });

  async function paramInput(): Promise<ParamInputComponent> {
    fixture.detectChanges();
    for (let attempt = 0; attempt < 20; attempt++) {
      await fixture.whenStable();
      fixture.detectChanges();
      const found = fixture.debugElement.query(By.directive(ParamInputComponent));
      if (found) return found.componentInstance as ParamInputComponent;
      await new Promise(resolve => setTimeout(resolve, 10));
    }
    throw new Error('the template field never rendered its input');
  }

  it('shows a single-line field with the template builder for the given template and type', async () => {
    const input = await paramInput();

    expect(input.value).toBe('CPU {{ vars.cpu }}');
    expect(input.resultType).toBe('numeric');
    expect(input.resultOptions).toEqual({ decimalPlaces: 2 });
    expect(input.showLiquid).toBeTrue();
  });

  it('reports every edit of the template', async () => {
    const input = await paramInput();
    const edits: string[] = [];
    fixture.componentInstance.valueChange.subscribe(value => edits.push(value));

    input.valueChange.emit('{{ vars.memory }}');

    expect(edits).toEqual(['{{ vars.memory }}']);
  });
});
