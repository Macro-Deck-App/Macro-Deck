import { provideZonelessChangeDetection, signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { ApiService, WidgetTypeInfo } from '../transport';
import {
  ActionButtonData,
  ActionFlow,
  ClockData,
  type CreateWidgetRequest,
  Folder,
  type FolderDeletedEvent,
  type GetFoldersResponse,
  GridRect,
  GridWidget,
  Profile,
  SliderData,
  WIDGET_GRID_VIEW_ID,
  WidgetClipboardEntry,
  type WidgetData,
  type WidgetPositionsUpdatedEvent,
  WidgetType,
} from '@macro-deck/runtime';
import { ActionExecutionService } from './action-execution.service';
import { FolderService } from './folder.service';
import { ProfileService } from './profile.service';
import { ToastService } from './toast.service';
import { WidgetTypeCatalogService } from './widget-type-catalog.service';

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

describe('FolderService widget layout', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;
  let currentProfile: Profile | null;

  const folderId = '11111111-1111-1111-1111-111111111111';
  const widgetAId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
  const widgetBId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

  function widget(id: string, x: number, y: number): GridWidget {
    return { id, folderId, x, y, w: 1, h: 1, type: WidgetType.ActionButton, data: { label: `label-${id}` } };
  }

  function folder(): Folder {
    return {
      id: folderId,
      name: 'F',
      parentId: null,
      order: 0,
      isExpanded: false,
      isDefault: true,
      cols: 5,
      rows: 3,
      background: '',
      spacing: null,
      borderRadius: null,
      viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null,
      widgets: [widget(widgetAId, 0, 0), widget(widgetBId, 1, 0)]
    };
  }

  beforeEach(() => {
    notifications = new Map();
    currentProfile = null;
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'reportFolderChanged',
      'updateWidgetPositions',
      'createWidget',
      'createWidgetFromApplication',
      'createWidgets',
      'deleteWidget',
      'deleteWidgets',
      'updateFolder',
      'updateWidget',
      'setWidgetsPinned',
      'onWidgetTypeCatalogChanged',
      'getWidgetTypes',
    ]);
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    apiSpy.updateWidgetPositions.and.resolveTo({ success: true });
    apiSpy.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    apiSpy.getWidgetTypes.and.resolveTo({ success: true, types: [] });
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    const profileServiceStub = { selectedProfileId: () => null, selectedProfile: () => currentProfile };

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ProfileService, useValue: profileServiceStub },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });

    service = TestBed.inject(FolderService);
    service.folders.set([folder()]);
    service.selectedFolderId.set(folderId);
  });

  function currentRect(id: string): GridRect {
    const w = service.folders()[0].widgets.find(x => x.id === id)!;
    return { x: w.x, y: w.y, w: w.w, h: w.h };
  }

  describe('commitWidgetLayout', () => {
    it('applies the changed rects optimistically and sends one batch request', async () => {
      const changes = new Map<string, GridRect>([
        [widgetAId, { x: 2, y: 1, w: 1, h: 1 }],
        [widgetBId, { x: 0, y: 0, w: 2, h: 2 }],
      ]);

      await service.commitWidgetLayout(changes);

      expect(currentRect(widgetAId)).toEqual({ x: 2, y: 1, w: 1, h: 1 });
      expect(currentRect(widgetBId)).toEqual({ x: 0, y: 0, w: 2, h: 2 });
      expect(apiSpy.updateWidgetPositions).toHaveBeenCalledTimes(1);
      expect(apiSpy.updateWidgetPositions).toHaveBeenCalledWith({
        folderId,
        positions: jasmine.arrayWithExactContents([
          { id: widgetAId, positionX: 2, positionY: 1, width: 1, height: 1 },
          { id: widgetBId, positionX: 0, positionY: 0, width: 2, height: 2 },
        ]),
      });
    });

    it('rolls back the optimistic update when the host rejects the batch', async () => {
      apiSpy.updateWidgetPositions.and.resolveTo({
        success: false,
        error: { code: 'PositionOccupied', message: 'nope' },
      });

      await service.commitWidgetLayout(new Map([[widgetAId, { x: 2, y: 1, w: 1, h: 1 }]]));

      expect(currentRect(widgetAId)).toEqual({ x: 0, y: 0, w: 1, h: 1 });
    });

    it('rolls back the optimistic update when the request throws', async () => {
      apiSpy.updateWidgetPositions.and.rejectWith(new Error('offline'));

      await service.commitWidgetLayout(new Map([[widgetAId, { x: 2, y: 1, w: 1, h: 1 }]]));

      expect(currentRect(widgetAId)).toEqual({ x: 0, y: 0, w: 1, h: 1 });
    });

    it('does nothing for an empty change set', async () => {
      await service.commitWidgetLayout(new Map());

      expect(apiSpy.updateWidgetPositions).not.toHaveBeenCalled();
    });
  });

  describe('WidgetPositionsUpdatedEvent', () => {
    it('merges rects into the folder and preserves local widget data', () => {
      const event: WidgetPositionsUpdatedEvent = {
        folderId,
        widgets: [{
          id: widgetAId,
          type: 'ActionButton' as never,
          positionX: 3,
          positionY: 2,
          width: 2,
          height: 1,
          data: '{"label":"from-host"}',
        }],
      };

      notifications.get('WidgetPositionsUpdatedEvent')!.next(event);

      const updated = service.folders()[0].widgets.find(w => w.id === widgetAId)!;
      expect(currentRect(widgetAId)).toEqual({ x: 3, y: 2, w: 2, h: 1 });
      expect(updated.data).toEqual({ label: `label-${widgetAId}` });
      expect(currentRect(widgetBId)).toEqual({ x: 1, y: 0, w: 1, h: 1 });
    });

    it('ignores events for other folders', () => {
      notifications.get('WidgetPositionsUpdatedEvent')!.next({
        folderId: '22222222-2222-2222-2222-222222222222',
        widgets: [{
          id: widgetAId,
          type: 'ActionButton' as never,
          positionX: 4,
          positionY: 2,
          width: 1,
          height: 1,
        }],
      });

      expect(currentRect(widgetAId)).toEqual({ x: 0, y: 0, w: 1, h: 1 });
    });
  });

  describe('batched widget events (issue #213)', () => {
    it('WidgetsCreatedEvent appends every widget not already present', () => {
      notifications.get('WidgetsCreatedEvent')!.next({
        folderId,
        widgets: [
          { id: 'new-a', type: 'ActionButton' as never, positionX: 3, positionY: 1, width: 1, height: 1, data: '{"label":"x"}' },
          // Already present - must not be duplicated.
          { id: widgetAId, type: 'ActionButton' as never, positionX: 0, positionY: 0, width: 1, height: 1 },
        ],
      });

      const ids = service.folders()[0].widgets.map(w => w.id);
      expect(ids.filter(id => id === 'new-a').length).toBe(1);
      expect(ids.filter(id => id === widgetAId).length).toBe(1);
      expect(currentRect('new-a')).toEqual({ x: 3, y: 1, w: 1, h: 1 });
    });

    it('WidgetsUpdatedEvent merges per widget, and a push with no data does not blank what we have', () => {
      notifications.get('WidgetsUpdatedEvent')!.next({
        folderId,
        widgets: [
          { id: widgetAId, type: 'ActionButton' as never, positionX: 2, positionY: 2, width: 1, height: 1 },
          { id: widgetBId, type: 'ActionButton' as never, positionX: 4, positionY: 2, width: 1, height: 1, data: '{"label":"from-host"}' },
        ],
      });

      const a = service.folders()[0].widgets.find(w => w.id === widgetAId)!;
      const b = service.folders()[0].widgets.find(w => w.id === widgetBId)!;
      expect(currentRect(widgetAId)).toEqual({ x: 2, y: 2, w: 1, h: 1 });
      expect(a.data).toEqual({ label: `label-${widgetAId}` });
      expect(currentRect(widgetBId)).toEqual({ x: 4, y: 2, w: 1, h: 1 });
      expect(b.data).toEqual(jasmine.objectContaining({ label: 'from-host' }));
    });

    it('WidgetsDeletedEvent removes every id in one filter', () => {
      notifications.get('WidgetsDeletedEvent')!.next({ folderId, widgetIds: [widgetAId, widgetBId] });

      expect(service.folders()[0].widgets).toEqual([]);
    });

    it('ignores a batched event for another folder', () => {
      notifications.get('WidgetsDeletedEvent')!.next({
        folderId: '22222222-2222-2222-2222-222222222222',
        widgetIds: [widgetAId],
      });

      expect(service.folders()[0].widgets.find(w => w.id === widgetAId)).toBeDefined();
    });
  });

  describe('addWidgetAt', () => {
    const pluginType = 'com.example.gauges::gauge';

    function catalogEntry(overrides: Partial<WidgetTypeInfo> & { id: string }): WidgetTypeInfo {
      return {
        providerId: 'com.example.gauges',
        isBuiltIn: false,
        defaultData: {},
        supportsConfigUi: false,
        configUiModelVersion: 0,
        ...overrides,
      };
    }

    async function seedCatalog(types: WidgetTypeInfo[]): Promise<void> {
      apiSpy.getWidgetTypes.and.resolveTo({ success: true, types });
      await TestBed.inject(WidgetTypeCatalogService).load();
    }

    beforeEach(() => {
      apiSpy.createWidget.and.callFake((req: CreateWidgetRequest) => Promise.resolve({
        success: true,
        widget: {
          id: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
          type: req.type,
          positionX: req.positionX,
          positionY: req.positionY,
          width: req.width,
          height: req.height,
          data: req.data,
        },
      }));
    });

    it("creates a plugin-provided widget with the qualified type id and the catalogue's default data", async () => {
      await seedCatalog([catalogEntry({ id: pluginType, defaultData: { min: 0, max: 100 } })]);

      const created = await service.addWidgetAt(2, 0, pluginType);

      expect(created).not.toBeNull();
      expect(created!.type).toBe(pluginType);
      expect(created!.data).toEqual({ min: 0, max: 100 } as unknown as WidgetData);
    });

    it('creates a widget with an empty object, never undefined, when the type has no default data', async () => {
      await seedCatalog([catalogEntry({ id: pluginType, defaultData: {} })]);

      const created = await service.addWidgetAt(2, 0, pluginType);

      expect(created).not.toBeNull();
      expect(created!.data).toEqual({} as unknown as WidgetData);
      expect(created!.data).not.toBeUndefined();
    });
  });

  describe('pasteWidget', () => {
    function clipboardEntry(overrides: Partial<WidgetClipboardEntry> = {}): WidgetClipboardEntry {
      return {
        type: WidgetType.ActionButton,
        w: 1,
        h: 1,
        data: { label: 'pasted' },
        dx: 0,
        dy: 0,
        isCut: false,
        origin: null,
        ...overrides,
      };
    }

    beforeEach(() => {
      apiSpy.createWidget.and.callFake((req: CreateWidgetRequest) => Promise.resolve({
        success: true,
        widget: {
          id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
          type: req.type,
          positionX: req.positionX,
          positionY: req.positionY,
          width: req.width,
          height: req.height,
          data: req.data,
        },
      }));
    });

    it('creates a widget at the anchor and keeps the size when it fits', async () => {
      const created = await service.pasteWidget(clipboardEntry({ w: 2, h: 2 }), 2, 0);

      expect(created).not.toBeNull();
      expect(apiSpy.createWidget).toHaveBeenCalledWith(jasmine.objectContaining({
        folderId,
        positionX: 2,
        positionY: 0,
        width: 2,
        height: 2,
      }));
      expect(currentRect(created!.id)).toEqual({ x: 2, y: 0, w: 2, h: 2 });
    });

    it('shrinks the paste to the space that is actually free', async () => {
      await service.pasteWidget(clipboardEntry({ w: 5, h: 1 }), 2, 0);

      expect(apiSpy.createWidget).toHaveBeenCalledWith(jasmine.objectContaining({
        positionX: 2,
        width: 3,
        height: 1,
      }));
    });

    it('does not call the host when the anchor cell is occupied', async () => {
      const created = await service.pasteWidget(clipboardEntry(), 0, 0);

      expect(created).toBeNull();
      expect(apiSpy.createWidget).not.toHaveBeenCalled();
    });

    it('excludes a same-folder cut source so the moved widget can keep its size', async () => {
      service.folders.set([{ ...folder(), widgets: [widget(widgetAId, 0, 0), widget(widgetBId, 3, 0)] }]);

      const cutEntry = clipboardEntry({ w: 3, h: 1, isCut: true, origin: { widgetId: widgetBId, folderId } });
      await service.pasteWidget(cutEntry, 2, 0);

      expect(apiSpy.createWidget).toHaveBeenCalledWith(jasmine.objectContaining({
        positionX: 2,
        width: 3,
        height: 1,
      }));
    });

    it('sends the origin widget id as sourceWidgetId so the host can clone its variables, for a copy or a cut', async () => {
      const copyEntry = clipboardEntry({ origin: { widgetId: widgetAId, folderId } });
      await service.pasteWidget(copyEntry, 2, 0);

      expect(apiSpy.createWidget).toHaveBeenCalledWith(jasmine.objectContaining({ sourceWidgetId: widgetAId }));
    });

    it('omits sourceWidgetId when the clipboard entry has no known origin', async () => {
      await service.pasteWidget(clipboardEntry({ origin: null }), 2, 0);

      expect(apiSpy.createWidget.calls.mostRecent().args[0].sourceWidgetId).toBeUndefined();
    });
  });

  describe('pasteWidgets (group paste, issue #213)', () => {
    function entry(overrides: Partial<WidgetClipboardEntry> = {}): WidgetClipboardEntry {
      return {
        type: WidgetType.ActionButton,
        w: 1,
        h: 1,
        data: { label: 'pasted' },
        dx: 0,
        dy: 0,
        isCut: false,
        origin: null,
        ...overrides,
      };
    }

    it('creates every member at anchor + its own offset, in one request', async () => {
      apiSpy.createWidgets.and.resolveTo({
        success: true,
        widgets: [
          { id: 'new-a', type: WidgetType.ActionButton, positionX: 2, positionY: 0, width: 1, height: 1, data: '{"label":"a"}' },
          { id: 'new-b', type: WidgetType.ActionButton, positionX: 3, positionY: 1, width: 1, height: 1, data: '{"label":"b"}' },
        ],
      });

      const result = await service.pasteWidgets([entry(), entry({ dx: 1, dy: 1 })], 2, 0);

      expect(result.success).toBeTrue();
      expect(apiSpy.createWidgets).toHaveBeenCalledTimes(1);
      expect(apiSpy.createWidgets).toHaveBeenCalledWith({
        folderId,
        widgets: [
          jasmine.objectContaining({ positionX: 2, positionY: 0, width: 1, height: 1 }),
          jasmine.objectContaining({ positionX: 3, positionY: 1, width: 1, height: 1 }),
        ],
      });
      expect(currentRect('new-a')).toEqual({ x: 2, y: 0, w: 1, h: 1 });
      expect(currentRect('new-b')).toEqual({ x: 3, y: 1, w: 1, h: 1 });
    });

    it('is rejected rather than shrunk when the group does not fit at the anchor', async () => {
      // Member offset (0,0) is 5 wide at anchor x=2 in a 5-col grid - it does not fit, and unlike
      // the single-widget path this must not shrink it to what does fit.
      const result = await service.pasteWidgets([entry({ w: 5, h: 1 })], 2, 0);

      expect(result.success).toBeFalse();
      expect(apiSpy.createWidgets).not.toHaveBeenCalled();
    });

    it('a rejected group paste leaves the folder widget list exactly as it was', async () => {
      const before = service.folders()[0].widgets;
      apiSpy.createWidgets.and.resolveTo({
        success: false,
        error: { code: 'ValidationError', message: 'rejected' },
      });

      const result = await service.pasteWidgets([entry(), entry({ dx: 1, dy: 0 })], 2, 0);

      expect(result.success).toBeFalse();
      expect(service.folders()[0].widgets).toEqual(before);
    });

    it('a group paste rejected by a thrown error also rolls back completely', async () => {
      const before = service.folders()[0].widgets;
      apiSpy.createWidgets.and.rejectWith(new Error('network down'));

      const result = await service.pasteWidgets([entry(), entry({ dx: 1, dy: 0 })], 2, 0);

      expect(result.success).toBeFalse();
      expect(service.folders()[0].widgets).toEqual(before);
    });

    it('excludes a same-folder cut source, same rule as the single-widget path', async () => {
      service.folders.set([{ ...folder(), widgets: [widget(widgetAId, 0, 0), widget(widgetBId, 3, 0)] }]);
      apiSpy.createWidgets.and.resolveTo({
        success: true,
        widgets: [{ id: 'moved', type: WidgetType.ActionButton, positionX: 2, positionY: 0, width: 3, height: 1, data: '{}' }],
      });

      const cutEntry = entry({ w: 3, h: 1, isCut: true, origin: { widgetId: widgetBId, folderId } });
      const result = await service.pasteWidgets([cutEntry], 2, 0);

      expect(result.success).toBeTrue();
      expect(apiSpy.createWidgets).toHaveBeenCalledWith(jasmine.objectContaining({
        widgets: [jasmine.objectContaining({ positionX: 2, width: 3 })],
      }));
    });

    it('sends a same-folder cut source as a replaceId in the SAME batch call, and reports it back', async () => {
      // Anchored exactly on top of widgetB, which the old whole-set overlap check would have
      // rejected - the client must ask the host to replace it atomically instead (issue #213).
      service.folders.set([{ ...folder(), widgets: [widget(widgetAId, 0, 0), widget(widgetBId, 1, 0)] }]);
      apiSpy.createWidgets.and.resolveTo({
        success: true,
        widgets: [{ id: 'new', type: WidgetType.ActionButton, positionX: 1, positionY: 0, width: 1, height: 1, data: '{}' }],
      });

      const cutEntry = entry({ isCut: true, origin: { widgetId: widgetBId, folderId } });
      const result = await service.pasteWidgets([cutEntry], 1, 0);

      expect(result.success).toBeTrue();
      expect(result.data?.replacedWidgetIds).toEqual([widgetBId]);
      expect(apiSpy.createWidgets).toHaveBeenCalledTimes(1);
      expect(apiSpy.createWidgets).toHaveBeenCalledWith(jasmine.objectContaining({ replaceIds: [widgetBId] }));
      expect(service.folders()[0].widgets.some(w => w.id === widgetBId)).toBeFalse();
    });

    it('a plain copy (never cut) sends no replaceIds', async () => {
      apiSpy.createWidgets.and.resolveTo({
        success: true,
        widgets: [{ id: 'new', type: WidgetType.ActionButton, positionX: 2, positionY: 0, width: 1, height: 1, data: '{}' }],
      });

      const result = await service.pasteWidgets([entry()], 2, 0);

      expect(result.success).toBeTrue();
      expect(result.data?.replacedWidgetIds).toEqual([]);
      expect(apiSpy.createWidgets.calls.mostRecent().args[0].replaceIds).toBeUndefined();
    });

    it('sends each member\'s origin widget id as its own sourceWidgetId, omitting it when there is none', async () => {
      apiSpy.createWidgets.and.resolveTo({
        success: true,
        widgets: [
          { id: 'new-a', type: WidgetType.ActionButton, positionX: 2, positionY: 0, width: 1, height: 1, data: '{}' },
          { id: 'new-b', type: WidgetType.ActionButton, positionX: 3, positionY: 1, width: 1, height: 1, data: '{}' },
        ],
      });

      const withOrigin = entry({ origin: { widgetId: widgetAId, folderId } });
      const withoutOrigin = entry({ dx: 1, dy: 1, origin: null });
      await service.pasteWidgets([withOrigin, withoutOrigin], 2, 0);

      const sent = apiSpy.createWidgets.calls.mostRecent().args[0].widgets;
      expect(sent[0].sourceWidgetId).toBe(widgetAId);
      expect(sent[1].sourceWidgetId).toBeUndefined();
    });
  });

  // The host decides whether a dropped file is an application at all, so there is nothing to insert
  // optimistically - only what it answers with (issue #395).
  describe('addWidgetFromApplication', () => {
    const createdId = 'dddddddd-dddd-dddd-dddd-dddddddddddd';

    it('inserts the button the host built and reports it', async () => {
      apiSpy.createWidgetFromApplication.and.resolveTo({
        success: true,
        widget: {
          id: createdId,
          type: WidgetType.ActionButton,
          positionX: 2,
          positionY: 1,
          width: 1,
          height: 1,
          data: JSON.stringify({ label: 'Calculator' }),
        },
      });

      const result = await service.addWidgetFromApplication(2, 1, '/Applications/Calculator.app');

      expect(apiSpy.createWidgetFromApplication).toHaveBeenCalledWith({
        folderId,
        positionX: 2,
        positionY: 1,
        path: '/Applications/Calculator.app',
      });
      expect(result.success).toBeTrue();
      expect(result.data!.id).toBe(createdId);
      expect(currentRect(createdId)).toEqual({ x: 2, y: 1, w: 1, h: 1 });
    });

    it('leaves the deck untouched when the host refuses the file', async () => {
      apiSpy.createWidgetFromApplication.and.resolveTo({
        success: false,
        error: { code: 'ValidationError', message: 'notes.md is not an application' },
      });

      const result = await service.addWidgetFromApplication(2, 1, '/tmp/notes.md');

      expect(result.success).toBeFalse();
      expect(result.error?.message).toBe('notes.md is not an application');
      expect(service.folders()[0].widgets.length).toBe(2);
    });
  });

  describe('removeWidget', () => {
    beforeEach(() => {
      apiSpy.deleteWidget.and.resolveTo({ success: true });
    });

    it('deletes from the selected folder by default', async () => {
      await service.removeWidget(widgetAId);

      expect(apiSpy.deleteWidget).toHaveBeenCalledWith({ id: widgetAId, folderId });
      expect(service.folders()[0].widgets.find(w => w.id === widgetAId)).toBeUndefined();
    });

    it('deletes from an explicit folder id for cross-folder cut cleanup', async () => {
      const otherFolderId = '99999999-9999-9999-9999-999999999999';

      await service.removeWidget(widgetAId, otherFolderId);

      expect(apiSpy.deleteWidget).toHaveBeenCalledWith({ id: widgetAId, folderId: otherFolderId });
      // The selected folder is untouched because the delete targeted another folder.
      expect(service.folders()[0].widgets.find(w => w.id === widgetAId)).toBeDefined();
    });
  });

  describe('removeWidgets (batch delete, issue #213)', () => {
    it('deletes every id in one request', async () => {
      apiSpy.deleteWidgets.and.resolveTo({ success: true });

      const result = await service.removeWidgets([widgetAId, widgetBId]);

      expect(result.success).toBeTrue();
      expect(apiSpy.deleteWidgets).toHaveBeenCalledTimes(1);
      expect(apiSpy.deleteWidgets).toHaveBeenCalledWith({ folderId, ids: [widgetAId, widgetBId] });
      expect(service.folders()[0].widgets).toEqual([]);
    });

    it('rolls back completely when the host rejects the batch (unlike removeWidget, which has no rollback)', async () => {
      const before = service.folders()[0].widgets;
      apiSpy.deleteWidgets.and.resolveTo({
        success: false,
        error: { code: 'NotFound', message: 'Widget not found' },
      });

      const result = await service.removeWidgets([widgetAId, widgetBId]);

      expect(result.success).toBeFalse();
      expect(service.folders()[0].widgets).toEqual(before);
    });

    it('rolls back completely when the request throws', async () => {
      const before = service.folders()[0].widgets;
      apiSpy.deleteWidgets.and.rejectWith(new Error('network down'));

      const result = await service.removeWidgets([widgetAId, widgetBId]);

      expect(result.success).toBeFalse();
      expect(service.folders()[0].widgets).toEqual(before);
    });

    it('deletes from an explicit folder id for cross-folder cleanup', async () => {
      const otherFolderId = '99999999-9999-9999-9999-999999999999';
      apiSpy.deleteWidgets.and.resolveTo({ success: true });

      await service.removeWidgets([widgetAId], otherFolderId);

      expect(apiSpy.deleteWidgets).toHaveBeenCalledWith({ folderId: otherFolderId, ids: [widgetAId] });
      expect(service.folders()[0].widgets.find(w => w.id === widgetAId)).toBeDefined();
    });
  });

  describe('updateWidget', () => {
    it('resolves false and does not keep the rejected data when the host rejects the write', async () => {
      apiSpy.updateWidget.and.resolveTo({
        success: false,
        error: { code: 'VALIDATION_ERROR', message: 'Label is too long' },
      });

      const result = await service.updateWidget(widgetAId, { data: { label: 'Way too long a label' } });

      expect(result).toBeFalse();
      const widget = service.folders()[0].widgets.find(w => w.id === widgetAId)!;
      expect(widget.data).toEqual({ label: `label-${widgetAId}` });
    });

    it('resolves false and rolls back when the request throws', async () => {
      apiSpy.updateWidget.and.rejectWith(new Error('offline'));

      const result = await service.updateWidget(widgetAId, { data: { label: 'New label' } });

      expect(result).toBeFalse();
      const widget = service.folders()[0].widgets.find(w => w.id === widgetAId)!;
      expect(widget.data).toEqual({ label: `label-${widgetAId}` });
    });

    it('resolves true and keeps the update when the host accepts the write', async () => {
      apiSpy.updateWidget.and.resolveTo({ success: true });

      const result = await service.updateWidget(widgetAId, { data: { label: 'New label' } });

      expect(result).toBeTrue();
      const widget = service.folders()[0].widgets.find(w => w.id === widgetAId)!;
      expect(widget.data).toEqual({ label: 'New label' });
    });
  });

  describe('slider data round-trip', () => {
    // Issue #425: a legacy bare iconId migrates to the typed icon reference at this same boundary,
    // and the migrated shape is what survives a second round trip - never the legacy key again.
    it('migrates iconId to the typed icon reference, and preserves showLabel/showValue across serialize/parse', () => {
      const data: SliderData = {
        orientation: 'vertical',
        label: 'Volume',
        color: '#123456',
        labelColor: '#ffffff',
        backgroundColor: '#000000',
        iconId: 'icon-guid-1',
        showLabel: false,
        showValue: true,
      };

      const json = (service as unknown as {
        serializeWidgetData(type: WidgetType, data: SliderData): string;
      }).serializeWidgetData(WidgetType.Slider, data);
      const parsed = (service as unknown as {
        parseWidgetData(type: WidgetType, data: string): SliderData;
      }).parseWidgetData(WidgetType.Slider, json);

      expect(parsed.icon).toEqual({ type: 'icon-pack', reference: 'icon-guid-1' });
      expect(parsed.iconId).toBeUndefined();
      expect(parsed.showLabel).toBe(false);
      expect(parsed.showValue).toBe(true);
      expect(parsed.orientation).toBe('vertical');
      expect(parsed.label).toBe('Volume');
      expect(parsed.color).toBe('#123456');
    });
  });

  describe('widget border round-trip', () => {
    function roundTrip<T>(type: WidgetType, data: T): T {
      const json = (service as unknown as {
        serializeWidgetData(type: WidgetType, data: T): string;
      }).serializeWidgetData(type, data);
      return (service as unknown as {
        parseWidgetData(type: WidgetType, data: string): T;
      }).parseWidgetData(type, json);
    }

    it('preserves a slider border across serialize/parse', () => {
      const parsed = roundTrip<SliderData>(WidgetType.Slider,
        { orientation: 'horizontal', border: { style: 'comet', color: '#22c55e' } });

      expect(parsed.border).toEqual({ style: 'comet', color: '#22c55e' });
    });

    it('preserves a clock border across serialize/parse', () => {
      const parsed = roundTrip<ClockData>(WidgetType.Clock,
        { style: 'digital', border: { style: 'rgb' } });

      expect(parsed.border).toEqual({ style: 'rgb' });
    });

    it('preserves per-state action button borders across serialize/parse', () => {
      const parsed = roundTrip<ActionButtonData>(WidgetType.ActionButton, {
        mode: 'toggle',
        states: {
          off: { backgroundColor: '#000000', border: { style: 'off' } },
          on: { backgroundColor: '#ef4444', border: { style: 'heartbeat', color: '#ffffff' } },
        },
      } as unknown as ActionButtonData);

      expect(parsed.states!.find(s => s.id === 'off')!.appearance!.border).toEqual({ style: 'off' });
      expect(parsed.states!.find(s => s.id === 'on')!.appearance!.border).toEqual({ style: 'heartbeat', color: '#ffffff' });
    });

    it('writes the border in the "on" state and emits no offState/onState aliases', () => {
      const json = (service as unknown as {
        serializeWidgetData(type: WidgetType, data: ActionButtonData): string;
      }).serializeWidgetData(WidgetType.ActionButton, {
        stateMode: true,
        states: [
          { id: 'off', label: 'Off', appearance: { backgroundColor: '#000000' } },
          { id: 'on', label: 'On', appearance: { backgroundColor: '#ef4444', border: { style: 'blink', color: '#ffffff' } } },
        ],
      } as unknown as ActionButtonData);

      const raw = JSON.parse(json) as {
        states: { id: string; appearance?: { border?: unknown } }[];
        offState?: unknown;
        onState?: unknown;
      };
      expect(raw.states.find(s => s.id === 'on')!.appearance?.border).toEqual({ style: 'blink', color: '#ffffff' });
      expect(raw.offState).toBeUndefined();
      expect(raw.onState).toBeUndefined();
    });
  });

  describe('widget spacing and border radius', () => {
    const parentId = '22222222-2222-2222-2222-222222222222';
    const grandParentId = '33333333-3333-3333-3333-333333333333';

    function folderWith(overrides: Partial<Folder>): Folder {
      return { ...folder(), widgets: [], ...overrides };
    }

    it('maps widgetSpacing/widgetBorderRadius from IPC, defaulting to null', () => {
      const map = (ipc: object): Folder => (service as unknown as {
        mapIpcFolder(ipc: object): Folder;
      }).mapIpcFolder({ id: 'x', name: 'X', order: 0, rows: 3, columns: 5, widgets: [], ...ipc });

      expect(map({ widgetSpacing: 6, widgetBorderRadius: 24 }).spacing).toBe(6);
      expect(map({ widgetSpacing: 6, widgetBorderRadius: 24 }).borderRadius).toBe(24);
      expect(map({}).spacing).toBeNull();
      expect(map({}).borderRadius).toBeNull();
    });

    it('uses the folder own values when set', () => {
      service.folders.set([folderWith({ spacing: 4, borderRadius: 30 })]);

      expect(service.currentSpacing()).toBe(4);
      expect(service.currentBorderRadius()).toBe(30);
    });

    it('inherits from the parent chain when the own values are null', () => {
      service.folders.set([
        folderWith({ id: grandParentId, spacing: 8, borderRadius: 18 }),
        folderWith({ id: parentId, parentId: grandParentId }),
        folderWith({ parentId }),
      ]);

      expect(service.currentSpacing()).toBe(8);
      expect(service.currentBorderRadius()).toBe(18);
    });

    it('falls back to the profile defaults when the folder chain is null', () => {
      currentProfile = stubProfile({ defaultSpacing: 16, defaultBorderRadius: 22 });
      service.folders.set([
        folderWith({ id: parentId }),
        folderWith({ parentId }),
      ]);

      expect(service.currentSpacing()).toBe(16);
      expect(service.currentBorderRadius()).toBe(22);
    });

    it('falls back to the built-ins when the folder chain and the profile are both null', () => {
      currentProfile = stubProfile();
      service.folders.set([
        folderWith({ id: parentId }),
        folderWith({ parentId }),
      ]);

      expect(service.currentSpacing()).toBe(12);
      expect(service.currentBorderRadius()).toBeNull();
    });

    it('terminates on a parentId cycle', () => {
      service.folders.set([
        folderWith({ id: parentId, parentId: folderId }),
        folderWith({ parentId }),
      ]);

      expect(service.currentSpacing()).toBe(12);
      expect(service.currentBorderRadius()).toBeNull();
    });

    it('setWidgetSpacing updates optimistically and sends the value', async () => {
      apiSpy.updateFolder.and.resolveTo({ success: true });

      await service.setWidgetSpacing(20);

      expect(service.folders()[0].spacing).toBe(20);
      expect(apiSpy.updateFolder).toHaveBeenCalledWith({ id: folderId, widgetSpacing: 20 });
    });

    it('setWidgetSpacing sends -1 to clear back to inherit', async () => {
      apiSpy.updateFolder.and.resolveTo({ success: true });
      service.folders.set([folderWith({ spacing: 20 })]);

      await service.setWidgetSpacing(null);

      expect(service.folders()[0].spacing).toBeNull();
      expect(apiSpy.updateFolder).toHaveBeenCalledWith({ id: folderId, widgetSpacing: -1 });
    });

    it('setWidgetBorderRadius updates optimistically and sends -1 for inherit', async () => {
      apiSpy.updateFolder.and.resolveTo({ success: true });

      await service.setWidgetBorderRadius(24);
      expect(service.folders()[0].borderRadius).toBe(24);
      expect(apiSpy.updateFolder).toHaveBeenCalledWith({ id: folderId, widgetBorderRadius: 24 });

      await service.setWidgetBorderRadius(null);
      expect(service.folders()[0].borderRadius).toBeNull();
      expect(apiSpy.updateFolder).toHaveBeenCalledWith({ id: folderId, widgetBorderRadius: -1 });
    });
  });

  describe('grid inheritance', () => {
    const parentId = '22222222-2222-2222-2222-222222222222';

    function folderWith(overrides: Partial<Folder>): Folder {
      return { ...folder(), widgets: [], ...overrides };
    }

    it('maps rows/columns from IPC, defaulting to null when absent', () => {
      const map = (ipc: object): Folder => (service as unknown as {
        mapIpcFolder(ipc: object): Folder;
      }).mapIpcFolder({ id: 'x', name: 'X', order: 0, widgets: [], ...ipc });

      expect(map({ rows: 3, columns: 5 }).rows).toBe(3);
      expect(map({ rows: 3, columns: 5 }).cols).toBe(5);
      expect(map({}).rows).toBeNull();
      expect(map({}).cols).toBeNull();
    });

    it('uses the folder own grid when set', () => {
      service.folders.set([folderWith({ cols: 8, rows: 6 })]);

      expect(service.currentCols()).toBe(8);
      expect(service.currentRows()).toBe(6);
    });

    it('inherits the grid from the parent chain when the own values are null', () => {
      service.folders.set([
        folderWith({ id: parentId, cols: 8, rows: 6 }),
        folderWith({ parentId, cols: null, rows: null }),
      ]);

      expect(service.currentCols()).toBe(8);
      expect(service.currentRows()).toBe(6);
    });

    it('falls back to the profile default when the folder chain is null', () => {
      currentProfile = stubProfile({ defaultColumns: 9, defaultRows: 7 });
      service.folders.set([
        folderWith({ id: parentId, cols: null, rows: null }),
        folderWith({ parentId, cols: null, rows: null }),
      ]);

      expect(service.currentCols()).toBe(9);
      expect(service.currentRows()).toBe(7);
    });

    it('falls back to the built-in grid when the folder chain and the profile are both absent', () => {
      service.folders.set([
        folderWith({ id: parentId, cols: null, rows: null }),
        folderWith({ parentId, cols: null, rows: null }),
      ]);

      expect(service.currentCols()).toBe(5);
      expect(service.currentRows()).toBe(3);
    });

    it('getEffectiveCols/getEffectiveRows resolve an arbitrary folder the same way as currentCols/currentRows', () => {
      service.folders.set([
        folderWith({ id: parentId, cols: 8, rows: 6 }),
        folderWith({ parentId, cols: null, rows: null }),
      ]);
      const child = service.getFolderById(folderId)!;

      expect(service.getEffectiveCols(child)).toBe(8);
      expect(service.getEffectiveRows(child)).toBe(6);
    });

    it('setGridDimensions sends -1 per axis to clear back to inherit', async () => {
      apiSpy.updateFolder.and.resolveTo({ success: true });
      service.folders.set([folderWith({ cols: 8, rows: 6 })]);

      await service.setGridDimensions(null, null);

      expect(service.folders()[0].cols).toBeNull();
      expect(service.folders()[0].rows).toBeNull();
      expect(apiSpy.updateFolder).toHaveBeenCalledWith({ id: folderId, columns: -1, rows: -1 });
    });

    it('setGridDimensions can set one axis while sending the raw (possibly inherited) value for the other', async () => {
      apiSpy.updateFolder.and.resolveTo({ success: true });
      service.folders.set([folderWith({ cols: null, rows: 6 })]);

      await service.setGridDimensions(9, null);

      expect(apiSpy.updateFolder).toHaveBeenCalledWith({ id: folderId, columns: 9, rows: -1 });
    });
  });
});

describe('FolderService deck-navigation triggers', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let selectedProfileId: WritableSignal<string | null>;
  let service: FolderService;

  const folderAId = '11111111-1111-1111-1111-111111111111';
  const folderBId = '22222222-2222-2222-2222-222222222222';
  const profileId = '99999999-9999-9999-9999-999999999999';

  function navFlows(targetFolderId: string): ActionFlow[] {
    return [{
      triggerId: 't1',
      triggerType: 'onShortPress',
      children: [{
        id: 'b1',
        type: 'action',
        blockType: 'app.macro-deck.deck.change-folder',
        label: 'Change Folder to',
        color: '#000',
        integrationId: 'app.macro-deck.deck',
        actionId: 'change-folder',
        parameters: [{ name: 'folderId', type: 'dynamic-choice', value: targetFolderId, label: 'Folder' }]
      }]
    }];
  }

  // Action Button no longer routes through this REST trigger path at all (#748) - it fires through
  // the generic widget-tree event pipeline instead. Clock still uses it, so the deck-navigation
  // shortcut this describe block exercises is tested against that type instead, unchanged otherwise.
  function navWidget(flows: ActionFlow[] | undefined): GridWidget {
    return {
      id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      folderId: folderAId,
      x: 0,
      y: 0,
      w: 1,
      h: 1,
      type: WidgetType.Clock,
      data: { flows } as ClockData
    };
  }

  function makeFolder(id: string, widgets: GridWidget[]): Folder {
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
      viewId: WIDGET_GRID_VIEW_ID,
      viewConfiguration: null,
      widgets
    };
  }

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService',
      ['onNotification', 'reportFolderChanged', 'executeActionButtonTrigger', 'getFolders', 'onWidgetTypeCatalogChanged'],
      { clientId: 'client-1' }
    );
    apiSpy.onNotification.and.returnValue(new Subject<never>().asObservable());
    apiSpy.executeActionButtonTrigger.and.resolveTo({ success: true, status: 'Succeeded' });
    apiSpy.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });
    selectedProfileId = signal<string | null>(null);

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ProfileService, useValue: { selectedProfileId, selectedProfile: () => null } },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });

    service = TestBed.inject(FolderService);
  });

  it('executes a pure change-folder flow locally without a host round trip', async () => {
    const widget = navWidget(navFlows(folderBId));
    service.folders.set([makeFolder(folderAId, [widget]), makeFolder(folderBId, [])]);
    service.selectedFolderId.set(folderAId);

    await service.executeActionButtonTrigger(widget, 'onShortPress');

    expect(service.selectedFolderId()).toBe(folderBId);
    expect(apiSpy.executeActionButtonTrigger).not.toHaveBeenCalled();
  });

  it('falls back to the host when the target folder is not loaded (cross-profile)', async () => {
    const widget = navWidget(navFlows('33333333-3333-3333-3333-333333333333'));
    service.folders.set([makeFolder(folderAId, [widget])]);
    service.selectedFolderId.set(folderAId);

    await service.executeActionButtonTrigger(widget, 'onShortPress');

    expect(service.selectedFolderId()).toBe(folderAId);
    expect(apiSpy.executeActionButtonTrigger).toHaveBeenCalled();
  });

  it('skips the host call for a trigger type that has no flow', async () => {
    const widget = navWidget(navFlows(folderBId));
    service.folders.set([makeFolder(folderAId, [widget]), makeFolder(folderBId, [])]);
    service.selectedFolderId.set(folderAId);

    await service.executeActionButtonTrigger(widget, 'onTouchStart');

    expect(apiSpy.executeActionButtonTrigger).not.toHaveBeenCalled();
  });

  it('skips the host call for a trigger configured with an empty action list (#480)', async () => {
    const widget = navWidget([
      { triggerId: 'onLongPress', triggerType: 'onLongPress', children: [] },
    ]);
    service.folders.set([makeFolder(folderAId, [widget])]);
    service.selectedFolderId.set(folderAId);

    await service.executeActionButtonTrigger(widget, 'onLongPress');

    expect(apiSpy.executeActionButtonTrigger).not.toHaveBeenCalled();
  });

  it('still sends the trigger when the flows are unknown', async () => {
    const widget = navWidget(undefined);
    service.folders.set([makeFolder(folderAId, [widget])]);
    service.selectedFolderId.set(folderAId);

    await service.executeActionButtonTrigger(widget, 'onShortPress');

    expect(apiSpy.executeActionButtonTrigger).toHaveBeenCalled();
  });

  it('does not refetch folders when the already-loaded profile becomes selected', async () => {
    apiSpy.getFolders.and.resolveTo({
      folders: [{
        id: folderAId,
        name: 'A',
        profileId,
        order: 0,
        rows: 3,
        columns: 5,
        widgets: []
      }]
    });

    await service.loadFolders();
    expect(apiSpy.getFolders).toHaveBeenCalledTimes(1);

    selectedProfileId.set(profileId);
    await Promise.resolve();
    TestBed.tick();
    expect(apiSpy.getFolders).toHaveBeenCalledTimes(1);

    selectedProfileId.set('88888888-8888-8888-8888-888888888888');
    TestBed.tick();
    expect(apiSpy.getFolders).toHaveBeenCalledTimes(2);
  });
});

describe('FolderService action execution outcome', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;
  let toasts: ToastService;

  const folderId = '11111111-1111-1111-1111-111111111111';
  const widgetId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

  // Clock, not Action Button: Action Button no longer routes execution outcomes through this REST
  // path at all (#748) - see ActionExecutionService's own generic-toast tests for its replacement.
  function widget(): GridWidget {
    return { id: widgetId, folderId, x: 0, y: 0, w: 1, h: 1, type: WidgetType.Clock, data: {} };
  }

  beforeEach(() => {
    // LocalizationService caches the active culture to localStorage, which - unlike TestBed - is not
    // reset between spec files sharing this Chrome instance. A test elsewhere that switches culture
    // would otherwise leak into these translated-text assertions.
    localStorage.clear();
    notifications = new Map();
    apiSpy = jasmine.createSpyObj<ApiService>(
      'ApiService',
      ['onNotification', 'reportFolderChanged', 'executeActionButtonTrigger', 'onWidgetTypeCatalogChanged'],
      { clientId: 'client-1' }
    );
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    apiSpy.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ProfileService, useValue: { selectedProfileId: () => null, selectedProfile: () => null } },
      ],
    });

    service = TestBed.inject(FolderService);
    toasts = TestBed.inject(ToastService);
    service.folders.set([{
      id: folderId,
      name: 'F',
      parentId: null,
      order: 0,
      isExpanded: false,
      isDefault: true,
      cols: 5,
      rows: 3,
      background: '',
      spacing: null,
      borderRadius: null,
      viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null,
      widgets: [widget()]
    }]);
    service.selectedFolderId.set(folderId);
  });

  it('stays silent on a succeeded response', async () => {
    apiSpy.executeActionButtonTrigger.and.resolveTo({ success: true, status: 'Succeeded', executionId: 'e1' });

    await service.executeActionButtonTrigger(widget(), 'onShortPress');

    expect(toasts.toasts()).toEqual([]);
  });

  it('toasts an error for a failed response', async () => {
    apiSpy.executeActionButtonTrigger.and.resolveTo({
      success: false, status: 'Failed', executionId: 'e2', error: { code: 'X', message: 'nope' },
    });

    await service.executeActionButtonTrigger(widget(), 'onShortPress');

    expect(toasts.toasts()).toEqual([jasmine.objectContaining({ variant: 'error', message: 'nope' })]);
  });

  // Issue #718: the host sends action errors as a LocalizedText, which can be an object
  // (`{ $localized: { scope, key } }`) rather than a plain string. Today's client interpolates it
  // raw, which renders `[object Object]`; the toast must show the translated sentence instead.
  it('resolves a localized-object error message to the translated sentence', async () => {
    apiSpy.executeActionButtonTrigger.and.resolveTo({
      success: false, status: 'Failed', executionId: 'e5',
      error: { code: 'X', message: { $localized: { scope: 'macrodeck', key: 'Common.Cancel' } } },
    });

    await service.executeActionButtonTrigger(widget(), 'onShortPress');

    expect(toasts.toasts()).toEqual([jasmine.objectContaining({ variant: 'error', message: 'Cancel' })]);
  });

  it('does not toast for an accepted response until the status event lands, then does', async () => {
    apiSpy.executeActionButtonTrigger.and.resolveTo({ success: true, status: 'Accepted', executionId: 'e3' });

    await service.executeActionButtonTrigger(widget(), 'onShortPress');
    expect(toasts.toasts()).toEqual([]);

    notifications.get('ActionExecutionStatusEvent')!.next({
      executionId: 'e3',
      status: 'Failed',
      durationMs: 500,
      actions: [],
      error: { code: 'X', message: 'ran late, failed' },
    });
    await Promise.resolve();
    await Promise.resolve();

    expect(toasts.toasts()).toEqual([
      jasmine.objectContaining({ variant: 'error', message: 'ran late, failed' }),
    ]);
  });

  it('does not toast for an accepted response that later succeeds', async () => {
    apiSpy.executeActionButtonTrigger.and.resolveTo({ success: true, status: 'Accepted', executionId: 'e4' });

    await service.executeActionButtonTrigger(widget(), 'onShortPress');

    notifications.get('ActionExecutionStatusEvent')!.next({
      executionId: 'e4',
      status: 'Succeeded',
      durationMs: 500,
      actions: [],
    });
    await Promise.resolve();
    await Promise.resolve();

    expect(toasts.toasts()).toEqual([]);
  });
});

describe('FolderService recursive delete', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;

  const rootId = '11111111-1111-1111-1111-111111111111';
  const childId = '22222222-2222-2222-2222-222222222222';
  const grandchildId = '33333333-3333-3333-3333-333333333333';
  const survivorId = '44444444-4444-4444-4444-444444444444';

  function makeFolder(overrides: Partial<Folder>): Folder {
    return {
      id: 'x',
      name: 'F',
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
      ...overrides,
    };
  }

  function makeWidget(id: string, folderId: string, isPinned = false): GridWidget {
    return { id, folderId, x: 0, y: 0, w: 1, h: 1, type: WidgetType.ActionButton, data: { label: id }, isPinned };
  }

  function buildTree(): Folder[] {
    return [
      makeFolder({ id: rootId, name: 'Root', parentId: null, isDefault: true, widgets: [makeWidget('w-root', rootId)] }),
      makeFolder({ id: childId, name: 'Child', parentId: rootId, widgets: [makeWidget('w-child', childId, true)] }),
      makeFolder({
        id: grandchildId, name: 'Grandchild', parentId: childId, widgets: [makeWidget('w-grandchild', grandchildId)]
      }),
      makeFolder({ id: survivorId, name: 'Survivor', parentId: null }),
    ];
  }

  beforeEach(() => {
    notifications = new Map();
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification',
      'reportFolderChanged',
      'deleteFolder',
      'getFolders',
      'onWidgetTypeCatalogChanged',
    ]);
    apiSpy.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    apiSpy.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    const profileServiceStub = { selectedProfileId: () => null, selectedProfile: () => null };

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ProfileService, useValue: profileServiceStub },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });

    service = TestBed.inject(FolderService);
    service.folders.set(buildTree());
  });

  it('deletionScope counts subfolders, widgets and pinned widgets across every level', () => {
    const scope = service.deletionScope(rootId);

    expect(scope).toEqual({ name: 'Root', subfolderCount: 2, widgetCount: 3, pinnedCount: 1 });
  });

  it('deletionScope returns null for an unknown folder', () => {
    expect(service.deletionScope('does-not-exist')).toBeNull();
  });

  it('canDeleteFolder is false when the subtree is the whole profile', () => {
    service.folders.set([
      makeFolder({ id: rootId, isDefault: true }),
      makeFolder({ id: childId, parentId: rootId }),
    ]);

    expect(service.canDeleteFolder(rootId)).toBeFalse();
  });

  it('deleteFolder removes the whole subtree and selects the parent', async () => {
    apiSpy.deleteFolder.and.resolveTo({ success: true });
    service.selectedFolderId.set(childId);

    const result = await service.deleteFolder(childId);

    expect(result.success).toBeTrue();
    expect(service.folders().map(f => f.id)).toEqual([rootId, survivorId]);
    expect(service.selectedFolderId()).toBe(rootId);
  });

  it('a host rejection leaves the tree untouched', async () => {
    apiSpy.deleteFolder.and.resolveTo({ success: false, error: { code: 'VALIDATION_ERROR', message: 'nope' } });

    const result = await service.deleteFolder(childId);

    expect(result.success).toBeFalse();
    expect(service.folders().map(f => f.id)).toEqual([rootId, childId, grandchildId, survivorId]);
  });

  it('a sequence of per-folder FolderDeletedEvents empties the whole subtree with no orphans', () => {
    for (const id of [grandchildId, childId, rootId]) {
      notifications.get('FolderDeletedEvent')!.next({ folderId: id } as FolderDeletedEvent);
    }

    expect(service.folders().map(f => f.id)).toEqual([survivorId]);
  });

  it('an event for the selected folder reselects the start folder', () => {
    service.folders.set([
      makeFolder({ id: childId, name: 'Child', parentId: null }),
      makeFolder({ id: grandchildId, name: 'Grandchild', parentId: childId }),
      makeFolder({ id: survivorId, name: 'Survivor', parentId: null, isDefault: true }),
    ]);
    service.selectedFolderId.set(grandchildId);

    notifications.get('FolderDeletedEvent')!.next({ folderId: grandchildId } as FolderDeletedEvent);

    expect(service.selectedFolderId()).toBe(survivorId);
  });

  it('prunes back-history of a deleted folder', () => {
    (service as unknown as { navHistory: Array<{ folderId: string; profileId: string | null }> }).navHistory = [
      { folderId: childId, profileId: null },
      { folderId: survivorId, profileId: null },
    ];

    notifications.get('FolderDeletedEvent')!.next({ folderId: childId } as FolderDeletedEvent);

    expect(
      (service as unknown as { navHistory: Array<{ folderId: string }> }).navHistory.map(entry => entry.folderId)
    ).toEqual([survivorId]);
  });

  it('a delete burst arriving during an in-flight loadFolders() does not resurrect the deleted subtree', async () => {
    let resolveGetFolders!: (response: GetFoldersResponse) => void;
    apiSpy.getFolders.and.returnValue(new Promise<GetFoldersResponse>(resolve => { resolveGetFolders = resolve; }));

    const loadPromise = service.loadFolders();

    for (const id of [grandchildId, childId, rootId]) {
      notifications.get('FolderDeletedEvent')!.next({ folderId: id } as FolderDeletedEvent);
    }

    resolveGetFolders({
      folders: [rootId, childId, grandchildId, survivorId].map(id => (
        { id, name: id, profileId: 'p', order: 0, rows: 3, columns: 5, widgets: [] }
      )),
    });
    await loadPromise;

    expect(service.folders().map(f => f.id)).toEqual([survivorId]);
  });
});
