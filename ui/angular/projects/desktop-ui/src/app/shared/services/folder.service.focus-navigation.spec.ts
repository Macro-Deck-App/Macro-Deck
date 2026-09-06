import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService } from '../transport';
import { Folder, type FolderNavigationEvent, WIDGET_GRID_VIEW_ID } from '@macro-deck/runtime';
import { ActionExecutionService } from './action-execution.service';
import { FolderService } from './folder.service';
import { ProfileService } from './profile.service';

describe('FolderService device-targeted navigation', () => {
  const folderAId = '11111111-1111-1111-1111-111111111111';
  const folderBId = '22222222-2222-2222-2222-222222222222';
  const folderCId = '33333333-3333-3333-3333-333333333333';

  let api: jasmine.SpyObj<ApiService>;
  let connectionState: ReturnType<typeof signal<string>>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;

  function push(method: string, payload: unknown): void {
    notifications.get(method)?.next(payload);
  }

  function makeFolder(id: string): Folder {
    return {
      id,
      name: `F-${id}`,
      parentId: null,
      order: 0,
      isExpanded: false,
      isDefault: false,
      cols: 5,
      rows: 3,
      background: '',
      spacing: null,
      borderRadius: null,
      viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null,
      widgets: [],
    };
  }

  async function settle(): Promise<void> {
    await Promise.resolve();
    TestBed.tick();
  }

  beforeEach(async () => {
    notifications = new Map();
    connectionState = signal<string>('disconnected');
    api = jasmine.createSpyObj<ApiService>('ApiService',
      ['onNotification', 'reportFolderChanged', 'onWidgetTypeCatalogChanged', 'getWidgetTypes']);
    api.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    api.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    api.getWidgetTypes.and.resolveTo({ success: true, types: [] });
    Object.defineProperty(api, 'connectionStateSignal', { value: connectionState });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ProfileService, useValue: { selectedProfileId: () => null } },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });
    service = TestBed.inject(FolderService);
    service.folders.set([makeFolder(folderAId), makeFolder(folderBId), makeFolder(folderCId)]);
    service.selectedFolderId.set(folderAId);
    await settle();
    api.reportFolderChanged.calls.reset();
  });

  function navigate(event: Partial<FolderNavigationEvent> & { command: string }): void {
    push('FolderNavigationEvent', event);
  }

  it('pushes the Back stack for a token-carrying changeTo, and echoes the token exactly once', async () => {
    navigate({ command: 'changeTo', folderId: folderBId, navigationToken: 'tok-1' });
    await settle();

    expect(service.selectedFolderId()).toBe(folderBId);
    expect(api.reportFolderChanged).toHaveBeenCalledTimes(1);
    expect(api.reportFolderChanged).toHaveBeenCalledWith(folderBId, 'tok-1');

    navigate({ command: 'back' });
    await settle();

    expect(service.selectedFolderId()).toBe(folderAId);
    expect(api.reportFolderChanged).toHaveBeenCalledWith(folderAId, undefined);
  });

  it('does not echo a token on a subsequent visit to the same folder', async () => {
    navigate({ command: 'changeTo', folderId: folderBId, navigationToken: 'tok-1' });
    await settle();
    api.reportFolderChanged.calls.reset();

    service.selectFolder(folderAId);
    await settle();
    service.selectFolder(folderBId);
    await settle();

    expect(api.reportFolderChanged).toHaveBeenCalledWith(folderBId, undefined);
  });

  it('reports a manual navigation without a token, and pushes it onto the Back stack', async () => {
    navigate({ command: 'changeTo', folderId: folderBId });
    await settle();

    expect(service.selectedFolderId()).toBe(folderBId);
    expect(api.reportFolderChanged).toHaveBeenCalledWith(folderBId, undefined);

    navigate({ command: 'back' });
    await settle();

    expect(service.selectedFolderId()).toBe(folderAId);
  });

  it('does not leak a pending token onto the report for a different folder', async () => {
    navigate({ command: 'changeTo', folderId: folderBId, navigationToken: 'tok-1' });
    service.selectFolder(folderCId);
    await settle();

    expect(api.reportFolderChanged).toHaveBeenCalledTimes(1);
    expect(api.reportFolderChanged).toHaveBeenCalledWith(folderCId, undefined);
  });

  it('sends a resync report on reconnect, without disrupting subsequent normal reports', async () => {
    connectionState.set('connected');
    await settle();

    expect(api.reportFolderChanged).toHaveBeenCalledWith(folderAId, undefined, true);
    api.reportFolderChanged.calls.reset();

    service.selectFolder(folderBId);
    await settle();

    expect(api.reportFolderChanged).toHaveBeenCalledWith(folderBId, undefined);
  });

  it('does not send a resync report when no folder is selected yet', async () => {
    service.selectedFolderId.set(null);
    await settle();
    api.reportFolderChanged.calls.reset();

    connectionState.set('connected');
    await settle();

    expect(api.reportFolderChanged).not.toHaveBeenCalled();
  });
});
