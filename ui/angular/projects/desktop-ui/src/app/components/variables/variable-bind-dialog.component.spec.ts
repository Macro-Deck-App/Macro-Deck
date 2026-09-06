import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { VariableCatalogNode } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import type { Variable } from '@macro-deck/runtime';
import { VariableBindDialogComponent } from './variable-bind-dialog.component';

function variable(id: string, name: string): Variable {
  return { id, name, scope: 'global', type: 'text', classification: 'user', value: '' };
}

describe('VariableBindDialogComponent', () => {
  let fixture: ComponentFixture<VariableBindDialogComponent>;
  let component: VariableBindDialogComponent;
  let bindSpy: jasmine.Spy;

  const node: VariableCatalogNode = {
    id: 'sensor.temperature', name: 'sensor.temperature', displayName: 'Living Room Temperature',
    hasChildren: false, type: 'numeric',
  };

  beforeEach(async () => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'bindCatalogVariable',
      'getVariables',
      'onNotification',
    ]);
    apiSpy.getVariables.and.resolveTo({ variables: [variable('existing', 'living_room_temperature')] });
    apiSpy.onNotification.and.callFake(() => new Subject());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('connected') });
    bindSpy = apiSpy.bindCatalogVariable;
    bindSpy.and.resolveTo({ variable: variable('new', 'living_room_temperature_2') });

    TestBed.configureTestingModule({
      imports: [VariableBindDialogComponent],
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });

    fixture = TestBed.createComponent(VariableBindDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('integrationId', 'home-assistant');
    fixture.componentRef.setInput('node', node);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('prefills the name from the resource and the type from its inferred type', () => {
    expect(component.rawName()).toBe('living_room_temperature');
    expect(component.type()).toBe('numeric');
  });

  it('disambiguates a generic display name using the node\'s own path (issue: every HA entity has a "state" leaf)', async () => {
    const stateLeaf: VariableCatalogNode = {
      id: 'entity/light.kitchen/state', name: 'state', displayName: 'State', hasChildren: false, type: 'text',
    };
    const otherFixture = TestBed.createComponent(VariableBindDialogComponent);
    otherFixture.componentRef.setInput('integrationId', 'home-assistant');
    otherFixture.componentRef.setInput('node', stateLeaf);
    otherFixture.detectChanges();
    await otherFixture.whenStable();

    expect(otherFixture.componentInstance.rawName()).toBe('light_kitchen_state');
  });

  it('flags a name that collides with an existing variable and blocks submission', async () => {
    component.onNameInput('living_room_temperature');
    await fixture.whenStable();

    expect(component.nameTaken()).toBeTrue();
    expect(component.canSubmit()).toBeFalse();
  });

  it('lets the user override the inferred type before binding', async () => {
    component.setType('text');
    component.onNameInput('living_room_temperature_2');
    await fixture.whenStable();

    await component.submit();

    expect(bindSpy).toHaveBeenCalledWith({
      integrationId: 'home-assistant',
      resourceId: 'sensor.temperature',
      name: 'living_room_temperature_2',
      type: 'text',
    });
  });
});
