import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ApiService } from '@shared';
import type { Variable } from '@macro-deck/runtime';
import { VariablePickerComponent } from './variable-picker.component';

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

describe('VariablePickerComponent', () => {
  let fixture: ComponentFixture<VariablePickerComponent>;
  let component: VariablePickerComponent;

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'getVariables',
      'getIntegrations',
      'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [] });
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      imports: [VariablePickerComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(VariablePickerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('variables', [variable({ id: 'u1', name: 'greeting' })]);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('opens the browser in pick mode from the trigger button', async () => {
    expect(fixture.nativeElement.querySelector('shared-variable-browser')).toBeNull();

    (fixture.nativeElement.querySelector('.vp-trigger') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(component.open()).toBeTrue();
    expect(fixture.nativeElement.querySelector('shared-variable-browser')).toBeTruthy();
  });

  it('emits the canonical variable name on pick and closes', async () => {
    const picked: string[] = [];
    component.pick.subscribe(name => picked.push(name));
    component.toggle();
    await fixture.whenStable();

    component.onPick(variable({ id: 'u1', name: 'greeting' }));

    expect(picked).toEqual(['greeting']);
    expect(component.open()).toBeFalse();
  });
});
