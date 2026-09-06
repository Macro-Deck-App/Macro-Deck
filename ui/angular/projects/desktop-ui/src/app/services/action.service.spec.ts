import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ActionParameterType } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ActionService, ActionDefinitionModel } from './action.service';

function model(id: string, integrationId: string, name: string): ActionDefinitionModel {
  return {
    id,
    integrationId,
    integrationName: integrationId,
    name,
    description: '',
    parameters: [],
  };
}

describe('ActionService', () => {
  let service: ActionService;
  let apiSpy: jasmine.SpyObj<ApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService', ['getActions', 'executeAction', 'getIntegrations', 'onNotification']);
    apiSpy.getIntegrations.and.resolveTo({ integrations: [] });
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(ActionService);
  });

  it('lists actions for one integration sorted by name', () => {
    service.actions.set([
      model('mute', 'system', 'Mute'),
      model('toggle', 'obs', 'Toggle Source'),
      model('set-volume', 'system', 'Set Volume'),
    ]);

    const result = service.actionsForIntegration('system');

    expect(result.map(a => a.id)).toEqual(['mute', 'set-volume']);
  });

  // The editors used to snapshot the palette after an await chain, so any earlier await that never
  // resolved left them with no actions at all (issue #189). Derived, it cannot be skipped.
  it('derives the action palette from the loaded actions', () => {
    expect(service.actionBlockDefinitions()).toEqual([]);

    service.actions.set([model('mute', 'system', 'Mute')]);

    const blocks = service.actionBlockDefinitions();
    expect(blocks.length).toBe(1);
    expect(blocks[0].blockType).toBe('system.mute');
    expect(blocks[0].category).toBe('system');
  });

  it('delegates runAction to the transport', async () => {
    apiSpy.executeAction.and.resolveTo({ success: true, durationMs: 5 });

    const response = await service.runAction('system', 'set-volume', { level: 42 });

    expect(apiSpy.executeAction).toHaveBeenCalledWith({
      integrationId: 'system',
      actionId: 'set-volume',
      parameters: { level: 42 },
    });
    expect(response.success).toBeTrue();
  });

  it('maps the action list from the transport with parameter defaults', async () => {
    apiSpy.getActions.and.resolveTo({
      actions: [
        {
          id: 'log-info',
          integrationId: 'example',
          integrationName: 'Example',
          name: 'Log Info',
          description: 'Logs a message',
          parameters: [{ name: 'message', type: ActionParameterType.String, description: '' }],
        },
      ],
    });

    await service.loadActions();

    expect(service.actions().length).toBe(1);
    expect(service.actions()[0].parameters[0].name).toBe('message');
  });
});
