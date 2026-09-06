import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ActionParameterType, ConfigFlowAuthorizedNotification, StartConfigFlowResponse, SubmitConfigFlowStepRequest, SubmitConfigFlowStepResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ConfigFlowService } from './config-flow.service';
import { ExternalLinkService } from './external-link.service';

const AUTH_URL = 'https://accounts.spotify.com/authorize?client_id=abc';

describe('ConfigFlowService', () => {
  let service: ConfigFlowService;
  let openedLinks: string[];
  let submitted: SubmitConfigFlowStepRequest[];
  let submitResponses: SubmitConfigFlowStepResponse[];
  let notifications: Subject<ConfigFlowAuthorizedNotification>;
  let editResponse: StartConfigFlowResponse;

  beforeEach(() => {
    openedLinks = [];
    submitted = [];
    submitResponses = [];
    notifications = new Subject<ConfigFlowAuthorizedNotification>();
    editResponse = {
      supported: true,
      flowId: 'flow-edit',
      step: {
        stepId: 'connection',
        fields: [
          { name: 'host', type: ActionParameterType.String, description: '', required: true },
          { name: 'password', type: ActionParameterType.Password, description: '', required: false },
        ],
      },
      initialValues: { host: 'studio.local' },
      storedSecretFields: ['password'],
    };

    const api = {
      startConfigFlow: (): Promise<StartConfigFlowResponse> =>
        Promise.resolve({
          supported: true,
          flowId: 'flow-1',
          step: { stepId: 'credentials', title: 'Connect Spotify', fields: [] },
        }),
      startEditConfigFlow: (): Promise<StartConfigFlowResponse> => Promise.resolve(editResponse),
      startNewConfigFlow: (): Promise<StartConfigFlowResponse> => Promise.resolve(editResponse),
      submitConfigFlowStep: (_integrationId: string, request: SubmitConfigFlowStepRequest) => {
        submitted.push(request);
        return Promise.resolve(submitResponses.shift() ?? { kind: 'Complete' as const });
      },
      onNotification: () => notifications.asObservable(),
    };

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ConfigFlowService,
        { provide: ApiService, useValue: api },
        { provide: ExternalLinkService, useValue: { open: (url: string) => openedLinks.push(url) } },
      ],
    });
    service = TestBed.inject(ConfigFlowService);
  });

  async function startAndSubmitCredentials(): Promise<void> {
    await service.start('spotify');
    await service.submit();
  }

  it('opens an External step through the external-link service', async () => {
    // window.open is dropped by the desktop shell's WebView, which left the consent page
    // unopened and the flow waiting forever (issue #133).
    submitResponses.push({ kind: 'External', externalUrl: AUTH_URL, resumeStepId: 'authorize' });

    await startAndSubmitCredentials();

    expect(openedLinks).toEqual([AUTH_URL]);
    expect(service.waitingForAuth()).toBeTrue();
  });

  it('submits the resume step once the authorization callback arrives', async () => {
    submitResponses.push({ kind: 'External', externalUrl: AUTH_URL, resumeStepId: 'authorize' });
    submitResponses.push({ kind: 'Complete' });

    await startAndSubmitCredentials();
    notifications.next({ flowId: 'flow-1', success: true });
    await Promise.resolve();

    expect(service.waitingForAuth()).toBeFalse();
    expect(submitted.map(request => request.stepId)).toEqual(['credentials', 'authorize']);
  });

  it('reports a failed authorization instead of resuming', async () => {
    submitResponses.push({ kind: 'External', externalUrl: AUTH_URL, resumeStepId: 'authorize' });

    await startAndSubmitCredentials();
    notifications.next({ flowId: 'flow-1', success: false, error: 'Access denied' });

    expect(service.message()).toBe('Access denied');
    expect(submitted.map(request => request.stepId)).toEqual(['credentials']);
  });

  it('opens nothing and reports an External step without a url', async () => {
    submitResponses.push({ kind: 'External', resumeStepId: 'authorize' });

    await startAndSubmitCredentials();

    expect(openedLinks).toEqual([]);
    expect(service.waitingForAuth()).toBeFalse();
    expect(service.message()).toBe('Authorization could not be started. Please try again.');
  });

  it('resolves localized config-flow messages before rendering them', async () => {
    submitResponses.push({
      kind: 'Error',
      message: {
        $localized: {
          scope: 'macrodeck.app',
          key: 'Integrations.Obs.Config.ConnectionFailed',
          arguments: {
            host: '127.0.0.1',
            port: 4455,
            details: {
              $localized: { scope: 'macrodeck.app', key: 'Integrations.Obs.Config.ConnectionRefused' },
            },
          },
        },
      },
      fieldErrors: {
        host: { $localized: { scope: 'macrodeck', key: 'Common.Done' } },
      },
    });

    await startAndSubmitCredentials();

    expect(service.message()).toBe('Could not connect to OBS at 127.0.0.1:4455. Connection refused.');
    expect(service.fieldErrors()).toEqual({ host: 'Done' });
  });

  it('resolves a localized transport error before rendering it', async () => {
    submitResponses.push({
      kind: 'Error',
      error: {
        code: 'FLOW_NOT_FOUND',
        message: { $localized: { scope: 'macrodeck', key: 'Common.Cancel' } },
      },
    });

    await startAndSubmitCredentials();

    expect(service.message()).toBe('Cancel');
  });

  it('does not treat an empty array as satisfying a required MultiSelect field', () => {
    service.step.set({
      stepId: 'entities',
      fields: [{ name: 'entities', type: ActionParameterType.MultiSelect, description: '', required: true }],
    });
    service.values.set({ entities: [] });
    expect(service.canSubmit()).toBeFalse();

    service.setValue('entities', ['light.kitchen']);
    expect(service.canSubmit()).toBeTrue();
  });

  it('edits with non-secret initial values while retaining a stored password without exposing it', async () => {
    await service.start('obs', { entryId: 'entry-a' });

    expect(service.values()).toEqual({ host: 'studio.local' });
    expect(service.storedSecretFields().has('password')).toBeTrue();

    await service.submit();

    expect(submitted[0].values).toEqual({ host: 'studio.local' });
    expect(submitted[0].clearedSecretFields).toEqual([]);
  });

  it('sends clearing a stored password as a distinct intent', async () => {
    await service.start('obs', { entryId: 'entry-a' });

    service.clearStoredSecret('password');
    await service.submit();

    expect(submitted[0].values['password']).toBeUndefined();
    expect(submitted[0].clearedSecretFields).toEqual(['password']);
  });
});
