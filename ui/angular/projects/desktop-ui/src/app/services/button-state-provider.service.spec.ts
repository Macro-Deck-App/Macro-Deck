import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NEVER } from 'rxjs';

import { adoptProvidedStates } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { ButtonStateProviderService } from './button-state-provider.service';

describe('ButtonStateProviderService', () => {
  let service: ButtonStateProviderService;
  let apiSpy: jasmine.SpyObj<ApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getActionButtonStateOptions', 'onNotification']);
    apiSpy.onNotification.and.returnValue(NEVER);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(ButtonStateProviderService);
  });

  it('resolves a localized-reference label to its translated sentence', async () => {
    apiSpy.getActionButtonStateOptions.and.resolveTo({
      states: [
        {
          id: 'muted',
          label: {
            $localized: { scope: 'macrodeck.app', key: 'Integrations.System.Actions.MuteVolume.MutedLabel' },
          },
        },
      ],
    });

    const result = await service.getStates('block-1', { integrationId: 'system', actionId: 'mute-volume' });

    expect(result).toEqual([jasmine.objectContaining({ id: 'muted', label: 'Muted' })]);
  });

  it('leaves an already-literal string label unchanged', async () => {
    apiSpy.getActionButtonStateOptions.and.resolveTo({
      states: [{ id: 'on', label: 'On' }],
    });

    const result = await service.getStates('block-2', { integrationId: 'custom', actionId: 'toggle' });

    expect(result).toEqual([jasmine.objectContaining({ id: 'on', label: 'On' })]);
  });

  it('adopts a localized provider state into stored widget data whose label is a plain string', async () => {
    apiSpy.getActionButtonStateOptions.and.resolveTo({
      states: [
        {
          id: 'unmuted',
          label: {
            $localized: { scope: 'macrodeck.app', key: 'Integrations.System.Actions.MuteVolume.UnmutedLabel' },
          },
        },
      ],
    });

    const fetched = await service.getStates('block-3', { integrationId: 'system', actionId: 'mute-volume' });
    const stored = adoptProvidedStates([], fetched ?? []);

    expect(stored).toEqual([{ id: 'unmuted', label: 'Unmuted', appearance: { label: 'Unmuted' } }]);
    expect(typeof stored[0].label).toBe('string');
  });
});
