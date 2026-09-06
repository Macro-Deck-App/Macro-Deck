import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NEVER } from 'rxjs';

import { GetActionProviderIconResponse } from '@macro-deck/runtime';
import { ApiService } from '@shared';
import { WidgetIconProviderService } from './widget-icon-provider.service';

describe('WidgetIconProviderService', () => {
  let service: WidgetIconProviderService;
  let apiSpy: jasmine.SpyObj<ApiService>;

  function snapshot(overrides: Partial<GetActionProviderIconResponse> = {}): GetActionProviderIconResponse {
    return { hasSnapshot: true, version: 'v1', noIcon: false, ...overrides };
  }

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getActionProviderIcon', 'onNotification']);
    apiSpy.onNotification.and.returnValue(NEVER);

    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: ApiService, useValue: apiSpy }],
    });
    service = TestBed.inject(WidgetIconProviderService);
  });

  it('resolves the host answer for a single request', async () => {
    apiSpy.getActionProviderIcon.and.resolveTo(snapshot({ reference: { type: 'icon-pack', reference: 'ICON_A' } }));

    const result = await service.getIconPreview('blk-1', { integrationId: 'i', actionId: 'a' });

    expect(result).toEqual(jasmine.objectContaining({ hasSnapshot: true }));
  });

  it('never lets a stale answer overwrite a newer request for the same block', async () => {
    let resolveFirst!: (value: GetActionProviderIconResponse) => void;
    const first = new Promise<GetActionProviderIconResponse>(resolve => { resolveFirst = resolve; });
    apiSpy.getActionProviderIcon.and.returnValues(
      first,
      Promise.resolve(snapshot({ version: 'v2' })),
    );

    const firstCall = service.getIconPreview('blk-1', { integrationId: 'i', actionId: 'a', parameters: { p: 1 } });
    const secondCall = service.getIconPreview('blk-1', { integrationId: 'i', actionId: 'a', parameters: { p: 2 } });

    const secondResult = await secondCall;
    resolveFirst(snapshot({ version: 'v1-stale' }));
    const firstResult = await firstCall;

    expect(firstResult).toBeNull();
    expect(secondResult).toEqual(jasmine.objectContaining({ version: 'v2' }));
  });

  it('de-duplicates concurrent in-flight requests for the same block, action and parameters', async () => {
    apiSpy.getActionProviderIcon.and.resolveTo(snapshot());

    await Promise.all([
      service.getIconPreview('blk-1', { integrationId: 'i', actionId: 'a', parameters: { p: 1 } }),
      service.getIconPreview('blk-1', { integrationId: 'i', actionId: 'a', parameters: { p: 1 } }),
    ]);

    expect(apiSpy.getActionProviderIcon).toHaveBeenCalledTimes(1);
  });

  it('reaches the host again for a later request once the earlier one has settled', async () => {
    apiSpy.getActionProviderIcon.and.resolveTo(snapshot());

    await service.getIconPreview('blk-1', { integrationId: 'i', actionId: 'a' });
    await service.getIconPreview('blk-1', { integrationId: 'i', actionId: 'a' });

    expect(apiSpy.getActionProviderIcon).toHaveBeenCalledTimes(2);
  });

  it('tracks superseding independently per block', async () => {
    apiSpy.getActionProviderIcon.and.resolveTo(snapshot());

    void service.getIconPreview('blk-1', { integrationId: 'i', actionId: 'a' });
    const other = await service.getIconPreview('blk-2', { integrationId: 'i', actionId: 'a' });

    expect(other).not.toBeNull();
  });
});
