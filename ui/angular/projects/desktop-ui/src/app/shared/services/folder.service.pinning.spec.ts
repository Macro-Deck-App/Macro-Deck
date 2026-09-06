import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService } from '../transport';
import {
  Folder,
  GridWidget,
  PinScope,
  Profile,
  WIDGET_GRID_VIEW_ID,
  WidgetType,
  type WidgetUpdatedEvent,
} from '@macro-deck/runtime';
import { ActionExecutionService } from './action-execution.service';
import { FolderService } from './folder.service';
import { ProfileService } from './profile.service';

function stubProfile(overrides: Partial<Profile> = {}): Profile {
  return {
    id: 'profile',
    name: 'P',
    order: 0,
    layoutType: 'Grid',
    isVirtual: false,
    layout: { rows: 3, columns: 5, rowsLocked: false, columnsLocked: false },
    defaultRows: 3,
    defaultColumns: 5,
    defaultBackground: null,
    defaultSpacing: null,
    defaultBorderRadius: null,
    ...overrides,
  };
}

describe('FolderService pinned widgets', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;

  const folderAId = '11111111-1111-1111-1111-111111111111';
  const folderBId = '22222222-2222-2222-2222-222222222222';
  const pinnedId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
  const plainId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

  function widget(id: string, folderId: string, x: number, isPinned = false, pinScope?: PinScope): GridWidget {
    return { id, folderId, x, y: 0, w: 1, h: 1, type: WidgetType.ActionButton, data: {}, isPinned, pinScope };
  }

  function folder(id: string, widgets: GridWidget[], parentId: string | null = null): Folder {
    return {
      id,
      name: `F-${id}`,
      parentId,
      order: 0,
      isExpanded: false,
      isDefault: false,
      cols: 5,
      rows: 3,
      background: '',
      spacing: null,
      borderRadius: null,
      viewId: WIDGET_GRID_VIEW_ID,
      viewConfiguration: null,
      widgets
    };
  }

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'reportFolderChanged',
      'setWidgetPinned',
      'updateWidgetData',
      'deleteWidget',
      'updateFolder',
      'onWidgetTypeCatalogChanged',
    ]);
    apiSpy.updateFolder.and.resolveTo({ success: true });
    notifications = new Map();
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    apiSpy.setWidgetPinned.and.resolveTo({ success: true });
    apiSpy.updateWidgetData.and.resolveTo({ success: true });
    apiSpy.deleteWidget.and.resolveTo({ success: true });
    apiSpy.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ProfileService, useValue: { selectedProfileId: () => null, selectedProfile: () => stubProfile() } },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });

    service = TestBed.inject(FolderService);
    service.folders.set([
      folder(folderAId, [widget(pinnedId, folderAId, 0, true)]),
      folder(folderBId, [widget(plainId, folderBId, 1)]),
    ]);
    service.selectedFolderId.set(folderBId);
  });

  it('picks up a pin made by another client from the pushed widget event', () => {
    const event: WidgetUpdatedEvent = {
      folderId: folderBId,
      widget: {
        id: plainId,
        type: WidgetType.ActionButton,
        positionX: 1,
        positionY: 0,
        width: 1,
        height: 1,
        isPinned: true,
      },
    };
    notifications.get('WidgetUpdatedEvent')!.next(event);

    expect(service.getFolderById(folderBId)!.widgets[0].isPinned).toBeTrue();
    service.selectedFolderId.set(folderAId);
    expect(service.currentWidgets().map(w => w.id)).toEqual([pinnedId, plainId]);
  });

  it('renders a pinned widget of another folder in the selected folder', () => {
    expect(service.currentWidgets().map(w => w.id)).toEqual([plainId, pinnedId]);
  });

  it('counts a pinned widget towards the minimum grid size', () => {
    service.folders.update(folders => folders.map(f => f.id === folderAId
      ? { ...f, widgets: [{ ...f.widgets[0], x: 3, y: 2 }] }
      : f));

    expect(service.minCols()).toBe(4);
    expect(service.minRows()).toBe(3);
  });

  it('persists runtime data of a pinned widget to its own folder, not the selected one', async () => {
    await service.updateWidgetRuntimeData(pinnedId, { label: 'Live' });

    expect(apiSpy.updateWidgetData).toHaveBeenCalledWith(
      jasmine.objectContaining({ widgetId: pinnedId, folderId: folderAId }));
  });

  it('deletes a pinned widget from its owning folder', async () => {
    await service.removeWidget(pinnedId);

    expect(apiSpy.deleteWidget).toHaveBeenCalledWith({ id: pinnedId, folderId: folderAId });
    expect(service.getFolderById(folderAId)!.widgets).toEqual([]);
  });

  it('pins through the owning folder and reflects the new flag locally', async () => {
    service.folders.update(folders => folders.map(f => f.id === folderAId
      ? { ...f, widgets: [{ ...f.widgets[0], isPinned: false }] }
      : f));

    const result = await service.setWidgetPinned(pinnedId, true);

    expect(result.success).toBeTrue();
    expect(apiSpy.setWidgetPinned).toHaveBeenCalledWith({ widgetId: pinnedId, folderId: folderAId, pinned: true });
    expect(service.getFolderById(folderAId)!.widgets[0].isPinned).toBeTrue();
  });

  it('rolls the grid back to its raw value - null included - when the host refuses the resize', async () => {
    service.folders.update(folders => folders.map(f =>
      f.id === folderBId ? { ...f, cols: null, rows: null } : f));
    apiSpy.updateFolder.and.resolveTo({
      success: false,
      error: { code: 'GridTooSmall', message: 'The grid must stay at least 5 x 3' },
    });

    const result = await service.setGridDimensions(2, 2);

    expect(result.success).toBeFalse();
    expect(result.error?.message).toContain('5 x 3');
    expect(service.getFolderById(folderBId)?.cols).toBeNull();
    expect(service.getFolderById(folderBId)?.rows).toBeNull();
    expect(service.currentCols()).toBe(5);
    expect(service.currentRows()).toBe(3);
  });

  it('keeps the local flag untouched and returns the host message when the pin is rejected', async () => {
    apiSpy.setWidgetPinned.and.resolveTo({
      success: false,
      error: { code: 'PositionOccupied', message: 'The widget does not fit at this position in: Games' },
    });

    const result = await service.setWidgetPinned(plainId, true);

    expect(result.success).toBeFalse();
    expect(result.error?.message).toContain('Games');
    expect(service.getFolderById(folderBId)!.widgets[0].isPinned).toBeFalsy();
  });

  it('forwards a chosen scope in the request', async () => {
    service.folders.update(folders => folders.map(f => f.id === folderAId
      ? { ...f, widgets: [{ ...f.widgets[0], isPinned: false, pinScope: undefined }] }
      : f));

    await service.setWidgetPinned(pinnedId, true, 'Subtree');

    expect(apiSpy.setWidgetPinned).toHaveBeenCalledWith(
      { widgetId: pinnedId, folderId: folderAId, pinned: true, scope: 'Subtree' });
  });

  it('I4: keeps the local scope untouched and returns the host message when a scope change is rejected', async () => {
    service.folders.update(folders => folders.map(f => f.id === folderAId
      ? { ...f, widgets: [{ ...f.widgets[0], isPinned: true, pinScope: 'Subtree' as PinScope }] }
      : f));
    apiSpy.setWidgetPinned.and.resolveTo({
      success: false,
      error: { code: 'PositionOccupied', message: 'Occupied in Mail' },
    });

    const result = await service.setWidgetPinned(pinnedId, true, 'Profile');

    expect(result.success).toBeFalse();
    expect(result.error?.message).toContain('Mail');
    const stillPinned = service.getFolderById(folderAId)!.widgets[0];
    expect(stillPinned.isPinned).toBeTrue();
    expect(stillPinned.pinScope).toBe('Subtree');
  });

  it('applies the response widget\'s own pinned/scope rather than the requested ones', async () => {
    apiSpy.setWidgetPinned.and.resolveTo({
      success: true,
      widget: {
        id: pinnedId,
        type: WidgetType.ActionButton,
        positionX: 0,
        positionY: 0,
        width: 1,
        height: 1,
        isPinned: true,
        pinScope: 'Profile',
      },
    });

    const result = await service.setWidgetPinned(pinnedId, true, 'Subtree');

    expect(result.success).toBeTrue();
    const updated = service.getFolderById(folderAId)!.widgets[0];
    expect(updated.isPinned).toBeTrue();
    expect(updated.pinScope).toBe('Profile');
  });
});

describe('FolderService pin scope reach (issue #245)', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;

  const mainId = '11111111-0000-0000-0000-000000000001';
  const gamesId = '11111111-0000-0000-0000-000000000002';
  const retroId = '11111111-0000-0000-0000-000000000003';
  const mediaId = '11111111-0000-0000-0000-000000000004';
  const workId = '11111111-0000-0000-0000-000000000005';
  const mailId = '11111111-0000-0000-0000-000000000006';

  function widget(id: string, folderId: string, x: number, y: number, isPinned = false, pinScope?: PinScope): GridWidget {
    return { id, folderId, x, y, w: 1, h: 1, type: WidgetType.ActionButton, data: {}, isPinned, pinScope };
  }

  function folder(id: string, parentId: string | null, widgets: GridWidget[]): Folder {
    return {
      id,
      name: `F-${id}`,
      parentId,
      order: 0,
      isExpanded: false,
      isDefault: false,
      cols: 5,
      rows: 3,
      background: '',
      spacing: null,
      borderRadius: null,
      viewId: WIDGET_GRID_VIEW_ID,
      viewConfiguration: null,
      widgets
    };
  }

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'reportFolderChanged',
      'setWidgetPinned',
      'updateWidgetData',
      'deleteWidget',
      'updateFolder',
      'onWidgetTypeCatalogChanged',
    ]);
    apiSpy.updateFolder.and.resolveTo({ success: true });
    notifications = new Map();
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    apiSpy.setWidgetPinned.and.resolveTo({ success: true });
    apiSpy.updateWidgetData.and.resolveTo({ success: true });
    apiSpy.deleteWidget.and.resolveTo({ success: true });
    apiSpy.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ProfileService, useValue: { selectedProfileId: () => null, selectedProfile: () => stubProfile() } },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });

    service = TestBed.inject(FolderService);
    service.folders.set([
      folder(mainId, null, []),
      folder(gamesId, mainId, []),
      folder(retroId, gamesId, []),
      folder(mediaId, mainId, []),
      folder(workId, null, []),
      folder(mailId, workId, []),
    ]);
  });

  it('H7: a subtree pin raises the reached descendants\' grid minimum but leaves an unrelated folder alone', () => {
    const w5 = widget('w5', mainId, 3, 3, true, 'Subtree');
    service.folders.update(folders => folders.map(f => f.id === mainId ? { ...f, widgets: [w5] } : f));

    service.selectedFolderId.set(retroId);
    expect(service.minCols()).toBeGreaterThanOrEqual(4);
    expect(service.minRows()).toBeGreaterThanOrEqual(4);

    service.selectedFolderId.set(mailId);
    expect(service.minCols()).toBe(1);
    expect(service.minRows()).toBe(1);
  });

  it('I5: a widget-updated event carrying a widened pin scope updates the local widget and mail loses reach', () => {
    const w = widget('w', mainId, 0, 0, true, 'Profile');
    service.folders.update(folders => folders.map(f => f.id === mainId ? { ...f, widgets: [w] } : f));
    service.selectedFolderId.set(mailId);
    expect(service.currentWidgets().map(x => x.id)).toContain('w');

    const event: WidgetUpdatedEvent = {
      folderId: mainId,
      widget: {
        id: 'w',
        type: WidgetType.ActionButton,
        positionX: 0,
        positionY: 0,
        width: 1,
        height: 1,
        isPinned: true,
        pinScope: 'Subtree',
      },
    };
    notifications.get('WidgetUpdatedEvent')!.next(event);

    expect(service.getFolderById(mainId)!.widgets[0].pinScope).toBe('Subtree');
    expect(service.currentWidgets().map(x => x.id)).not.toContain('w');
  });
});
