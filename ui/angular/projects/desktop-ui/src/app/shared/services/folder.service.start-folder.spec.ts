import { provideZonelessChangeDetection, signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService } from '../transport';
import {
  Folder,
  type FolderCreatedEvent,
  type FolderDeletedEvent,
  type IpcFolder,
  type IpcFolderPlacement,
  WIDGET_GRID_VIEW_ID,
} from '@macro-deck/runtime';
import { ActionExecutionService } from './action-execution.service';
import { FolderService } from './folder.service';
import { ProfileService } from './profile.service';

describe('FolderService start-folder selection', () => {
  const startId = '11111111-1111-1111-1111-111111111111';
  const otherRootId = '22222222-2222-2222-2222-222222222222';
  const childId = '33333333-3333-3333-3333-333333333333';
  let api: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;
  let selectedProfileId: WritableSignal<string | null>;

  function folder(id: string, order: number, isDefault: boolean, parentId?: string): IpcFolder {
    return {
      id,
      name: id,
      profileId: 'profile',
      parentId,
      order,
      rows: 3,
      columns: 5,
      isDefault,
      widgets: [],
    };
  }

  function domainFolder(id: string, order: number, isDefault: boolean, parentId: string | null = null): Folder {
    return {
      id,
      name: id,
      parentId,
      order,
      isExpanded: true,
      isDefault,
      cols: 5,
      rows: 3,
      background: '',
      spacing: null,
      borderRadius: null,
      viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null,
      widgets: [],
    };
  }

  beforeEach(() => {
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'reportFolderChanged', 'getFolders', 'updateFolder', 'moveFolder', 'onWidgetTypeCatalogChanged',
    ]);
    api.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    api.updateFolder.and.resolveTo({ success: true });
    api.moveFolder.and.resolveTo({ success: true });
    api.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });

    selectedProfileId = signal<string | null>(null);
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ProfileService, useValue: { selectedProfileId, selectedProfile: () => null } },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });
    service = TestBed.inject(FolderService);
  });

  it('selects the persisted start marker even when sibling order would pick another root', async () => {
    api.getFolders.and.resolveTo({ folders: [
      folder(otherRootId, 0, false),
      folder(startId, 1, true),
    ] });

    await service.loadFolders('profile');

    expect(service.selectedFolderId()).toBe(startId);
    expect(service.startFolderId()).toBe(startId);
  });

  it('breaks a root order tie ordinally, not with a locale-aware compare (issue #251: mirrors the host StartFolderResolver)', async () => {
    api.getFolders.and.resolveTo({ folders: [
      folder('alpha', 0, false),
      folder('Beta', 0, false),
    ] });

    await service.loadFolders('profile');

    expect(service.selectedFolderId()).toBe('Beta');
    expect(service.startFolderId()).toBe('Beta');
  });

  it('keeps a scoped tree free of created folders from another profile', async () => {
    api.getFolders.and.resolveTo({ folders: [folder(startId, 0, true)] });
    await service.loadFolders('profile');

    notifications.get('FolderCreatedEvent')!.next({
      folder: { ...folder('44444444-4444-4444-4444-444444444444', 0, true), profileId: 'imported-profile' }
    } satisfies FolderCreatedEvent);
    notifications.get('FolderCreatedEvent')!.next({
      folder: folder(otherRootId, 1, false)
    } satisfies FolderCreatedEvent);

    expect(service.folders().map(current => current.id)).toEqual([startId, otherRootId]);
    expect(service.startFolderId()).toBe(startId);
  });

  it('merges a current-profile create received before its scoped load response', async () => {
    let resolveFolders: (response: { folders: IpcFolder[] }) => void;
    api.getFolders.and.returnValue(new Promise(resolve => {
      resolveFolders = resolve;
    }));

    const loading = service.loadFolders('profile');
    notifications.get('FolderCreatedEvent')!.next({
      folder: folder(otherRootId, 1, false)
    } satisfies FolderCreatedEvent);
    resolveFolders!({ folders: [folder(startId, 0, true)] });
    await loading;

    expect(service.folders().map(current => current.id)).toEqual([startId, otherRootId]);
    expect(service.startFolderId()).toBe(startId);
  });

  it('shares the profile-effect request with the load awaited by web initialization', async () => {
    let resolveFolders: (response: { folders: IpcFolder[] }) => void;
    api.getFolders.and.returnValue(new Promise(resolve => {
      resolveFolders = resolve;
    }));

    selectedProfileId.set('profile');
    TestBed.tick();
    const initialized = service.loadFolders('profile');

    expect(api.getFolders).toHaveBeenCalledTimes(1);
    resolveFolders!({ folders: [folder(startId, 0, true)] });
    await initialized;
  });

  it('selects the replacement marker when the current start folder is deleted by another client', () => {
    service.folders.set([
      domainFolder(startId, 1, true),
      domainFolder(otherRootId, 0, true),
    ]);
    service.selectedFolderId.set(startId);

    notifications.get('FolderDeletedEvent')!.next({ folderId: startId } satisfies FolderDeletedEvent);

    expect(service.selectedFolderId()).toBe(otherRootId);
  });

  it('rolls back an optimistic start marker when the host rejects it', async () => {
    service.folders.set([
      domainFolder(startId, 0, true),
      domainFolder(otherRootId, 1, false),
    ]);
    api.updateFolder.and.resolveTo({
      success: false,
      error: { code: 'ValidationError', message: 'folder moved before the request' },
    });

    await service.setStartFolder(otherRootId);

    expect(service.getFolderById(startId)?.isDefault).toBeTrue();
    expect(service.getFolderById(otherRootId)?.isDefault).toBeFalse();
  });

  it('moving a folder back to the root level targets a root folder directly, with no sentinel', async () => {
    service.folders.set([
      domainFolder(otherRootId, 0, true),
      domainFolder(childId, 0, false, otherRootId),
    ]);

    await service.moveFolder({ folderId: childId, targetId: otherRootId, position: 'after' });

    expect(api.moveFolder).toHaveBeenCalledWith({ id: childId, targetId: otherRootId, position: 'after' });
    expect(service.getFolderById(childId)?.parentId).toBeNull();
  });

  it('rolls back an optimistic folder move when the host rejects it', async () => {
    service.folders.set([
      domainFolder(otherRootId, 0, true),
      domainFolder(childId, 0, false, otherRootId),
    ]);
    api.moveFolder.and.resolveTo({
      success: false,
      error: { code: 'InvalidParent', message: 'move rejected' },
    });

    await service.moveFolder({ folderId: childId, targetId: otherRootId, position: 'after' });

    expect(service.getFolderById(childId)?.parentId).toBe(otherRootId);
  });

  it('defers a start-folder move into another folder until the host responds, so a client never ' +
    'renders a child as the start folder with no replacement marker', async () => {
    service.folders.set([
      domainFolder(startId, 0, true),
      domainFolder(otherRootId, 1, false),
    ]);
    let resolveMove!: (response: { success: boolean; folders?: IpcFolderPlacement[] }) => void;
    api.moveFolder.and.returnValue(new Promise(resolve => {
      resolveMove = resolve;
    }));

    const moving = service.moveFolder({ folderId: startId, targetId: otherRootId, position: 'inside' });

    expect(service.getFolderById(startId)?.parentId).toBeNull();

    resolveMove({
      success: true,
      folders: [
        { id: startId, parentId: otherRootId, order: 0, isDefault: false },
        { id: otherRootId, parentId: null, order: 0, isDefault: true },
      ],
    });
    await moving;

    expect(service.getFolderById(startId)?.parentId).toBe(otherRootId);
    expect(service.getFolderById(otherRootId)?.isDefault).toBeTrue();
  });
});
