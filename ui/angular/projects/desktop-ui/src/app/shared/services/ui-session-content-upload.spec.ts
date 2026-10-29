import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { NEVER } from 'rxjs';

import { UiContentUploadResponse } from '@macro-deck/runtime';
import { UiSessionHandle, UiSessionService } from './ui-session.service';
import { ToastService } from './toast.service';
import { ApiService } from '../transport';
import { LocalizationService } from '../localization';

describe('UiSessionService page sessions and content uploads', () => {
  let api: jasmine.SpyObj<ApiService>;
  let finishUpload: (response: UiContentUploadResponse | Error) => void;

  beforeEach(() => {
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'openPageUiSession',
      'attachUiSession',
      'closeUiSession',
      'sendUiEvent',
      'uploadUiContent',
      'onUiSessionTreeUpdated',
      'onUiSessionPatched',
      'onUiSessionInvalidated',
      'onUiSessionClosed',
      'onUiSessionParameterValuesReported',
      'onUiSessionFlushed',
    ]);
    Object.defineProperty(api, 'connectionState$', { value: NEVER });
    api.onUiSessionTreeUpdated.and.returnValue(NEVER);
    api.onUiSessionPatched.and.returnValue(NEVER);
    api.onUiSessionInvalidated.and.returnValue(NEVER);
    api.onUiSessionClosed.and.returnValue(NEVER);
    api.onUiSessionParameterValuesReported.and.returnValue(NEVER);
    api.onUiSessionFlushed.and.returnValue(NEVER);
    api.openPageUiSession.and.resolveTo({ accepted: true, sessionId: 's1' });
    api.attachUiSession.and.resolveTo({ accepted: true, sessionId: 's1', surfaceKind: 'page', revision: 1 } as never);
    api.sendUiEvent.and.resolveTo(null);
    api.uploadUiContent.and.callFake(() => new Promise<UiContentUploadResponse>((resolve, reject) => {
      finishUpload = response => (response instanceof Error ? reject(response) : resolve(response));
    }));

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        UiSessionService,
        { provide: ApiService, useValue: api },
        { provide: ToastService, useValue: jasmine.createSpyObj<ToastService>('ToastService', ['show']) },
        { provide: LocalizationService, useValue: { translateKey: (key: string) => key } },
      ],
    });
  });

  async function openPage(): Promise<UiSessionHandle> {
    const handle = TestBed.inject(UiSessionService).open({ kind: 'page', integrationId: 'app.example', pageId: 'scripts' });
    await new Promise(resolve => setTimeout(resolve));
    return handle;
  }

  function sentEvents(): string[] {
    return api.sendUiEvent.calls.allArgs().map(([event]) => `${event.name} ${event.nodeId}`);
  }

  it('opens a page session for the integration page', async () => {
    await openPage();

    expect(api.openPageUiSession)
      .toHaveBeenCalledOnceWith(jasmine.objectContaining({ integrationId: 'app.example', pageId: 'scripts' }));
    expect(api.attachUiSession).toHaveBeenCalledWith('s1');
  });

  it('holds a click made during an upload until the uploaded event went out', async () => {
    const handle = await openPage();

    const upload = handle.uploadContent!('source', 'MsgBox "hi"');
    handle.send({ nodeId: 'save', name: 'activate' });
    expect(sentEvents()).toEqual([]);

    finishUpload({ uploadId: 'u1', length: 11, contentHash: 'sha256:abc' });
    await upload;

    expect(api.uploadUiContent).toHaveBeenCalledOnceWith('s1', 'source', 'MsgBox "hi"');
    expect(sentEvents()).toEqual(['content-uploaded source', 'activate save']);
    expect(api.sendUiEvent.calls.first().args[0].data)
      .toEqual({ uploadId: 'u1', length: 11, contentHash: 'sha256:abc' });
  });

  it('reports a failed upload and then releases the held click', async () => {
    const handle = await openPage();

    const upload = handle.uploadContent!('source', 'x'.repeat(10));
    handle.send({ nodeId: 'save', name: 'activate' });
    finishUpload(new Error('413'));

    expect(await upload).toBeNull();
    expect(sentEvents()).toEqual(['content-upload-failed source', 'activate save']);
    expect(api.sendUiEvent.calls.first().args[0].data).toBeUndefined();
  });
});
