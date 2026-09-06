import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService } from '../transport';
import {
  Folder,
  type FoldersReorderedEvent,
  GridWidget,
  WIDGET_GRID_VIEW_ID,
  WidgetType,
} from '@macro-deck/runtime';
import { ActionExecutionService } from './action-execution.service';
import { FolderService } from './folder.service';
import { ProfileService } from './profile.service';

describe('FolderService.moveFolder', () => {
  const rootA = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
  const rootB = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
  const rootC = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
  const childA1 = '11111111-1111-1111-1111-111111111111';
  const childA2 = '22222222-2222-2222-2222-222222222222';

  let api: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;

  function widget(id: string, folderId: string): GridWidget {
    return { id, folderId, x: 0, y: 0, w: 1, h: 1, type: WidgetType.ActionButton, data: {} };
  }

  function folder(overrides: Partial<Folder> & { id: string; order: number }): Folder {
    return {
      name: overrides.id,
      parentId: null,
      isExpanded: true,
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

  function emitReordered(event: FoldersReorderedEvent): void {
    notifications.get('FoldersReorderedEvent')!.next(event);
  }

  beforeEach(() => {
    notifications = new Map();
    api = jasmine.createSpyObj<ApiService>('ApiService', [
      'onNotification', 'reportFolderChanged', 'moveFolder', 'getFolders', 'onWidgetTypeCatalogChanged',
    ]);
    api.onNotification.and.callFake(<T>(method: string): Observable<T> => {
      let subject = notifications.get(method);
      if (!subject) {
        subject = new Subject<unknown>();
        notifications.set(method, subject);
      }
      return subject.asObservable() as Observable<T>;
    });
    api.moveFolder.and.resolveTo({ success: true });
    api.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(api, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: api },
        { provide: ProfileService, useValue: { selectedProfileId: () => null, selectedProfile: () => null } },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });
    service = TestBed.inject(FolderService);
  });

  it('posts the resolved id/targetId/position shape to the host', async () => {
    service.folders.set([folder({ id: rootA, order: 0 }), folder({ id: rootB, order: 1 })]);

    await service.moveFolder({ folderId: rootB, targetId: rootA, position: 'before' });

    expect(api.moveFolder).toHaveBeenCalledWith({ id: rootB, targetId: rootA, position: 'before' });
  });

  it('renumbers siblings densely with no duplicate orders after an optimistic reorder', async () => {
    service.folders.set([
      folder({ id: rootA, order: 0 }),
      folder({ id: rootB, order: 1 }),
      folder({ id: rootC, order: 2 }),
    ]);
    api.moveFolder.and.returnValue(new Promise(() => {}));

    void service.moveFolder({ folderId: rootC, targetId: rootA, position: 'before' });

    const orders = [rootA, rootB, rootC].map(id => service.getFolderById(id)!.order).sort();
    expect(orders).toEqual([0, 1, 2]);
    expect(service.getFolderById(rootC)?.order).toBe(0);
    expect(service.getFolderById(rootA)?.order).toBe(1);
    expect(service.getFolderById(rootB)?.order).toBe(2);
  });

  it('reparenting also compacts the vacated source siblings to 0..n-1', async () => {
    service.folders.set([
      folder({ id: rootA, order: 0 }),
      folder({ id: rootB, order: 1 }),
      folder({ id: childA1, order: 0, parentId: rootA }),
      folder({ id: childA2, order: 1, parentId: rootA }),
    ]);
    api.moveFolder.and.returnValue(new Promise(() => {}));

    void service.moveFolder({ folderId: childA1, targetId: rootB, position: 'inside' });

    expect(service.getFolderById(childA1)?.order).toBe(0);
    expect(service.getFolderById(childA1)?.parentId).toBe(rootB);
    expect(service.getFolderById(childA2)?.order).toBe(0);
  });

  it('inside expands the target folder', async () => {
    service.folders.set([
      folder({ id: rootA, order: 0, isExpanded: false }),
      folder({ id: rootB, order: 1 }),
    ]);

    await service.moveFolder({ folderId: rootB, targetId: rootA, position: 'inside' });

    expect(service.getFolderById(rootA)?.isExpanded).toBeTrue();
  });

  it('moving a nested folder out lands it at the root, with no root sentinel sent', async () => {
    service.folders.set([
      folder({ id: rootA, order: 0 }),
      folder({ id: childA1, order: 0, parentId: rootA }),
    ]);

    const request = service.resolveMove(childA1, 'out');
    expect(request).toEqual({ folderId: childA1, targetId: rootA, position: 'after' });

    await service.moveFolder(request!);

    expect(api.moveFolder).toHaveBeenCalledWith({ id: childA1, targetId: rootA, position: 'after' });
    expect(service.getFolderById(childA1)?.parentId).toBeNull();
  });

  it('rejects moving a folder into its own descendant, persisting nothing', async () => {
    service.folders.set([
      folder({ id: rootA, order: 0 }),
      folder({ id: childA1, order: 0, parentId: rootA }),
    ]);

    const result = await service.moveFolder({ folderId: rootA, targetId: childA1, position: 'inside' });

    expect(result.success).toBeFalse();
    expect(api.moveFolder).not.toHaveBeenCalled();
    expect(service.getFolderById(rootA)?.parentId).toBeNull();
  });

  it('rolls back the optimistic move when the host reports failure', async () => {
    service.folders.set([folder({ id: rootA, order: 0 }), folder({ id: rootB, order: 1 })]);
    api.moveFolder.and.resolveTo({ success: false, error: { code: 'InvalidParent', message: 'no' } });

    const result = await service.moveFolder({ folderId: rootB, targetId: rootA, position: 'before' });

    expect(result.success).toBeFalse();
    expect(service.getFolderById(rootA)?.order).toBe(0);
    expect(service.getFolderById(rootB)?.order).toBe(1);
  });

  it('rolls back the optimistic move when the request throws', async () => {
    service.folders.set([folder({ id: rootA, order: 0 }), folder({ id: rootB, order: 1 })]);
    api.moveFolder.and.rejectWith(new Error('network down'));

    const result = await service.moveFolder({ folderId: rootB, targetId: rootA, position: 'before' });

    expect(result.success).toBeFalse();
    expect(service.getFolderById(rootA)?.order).toBe(0);
    expect(service.getFolderById(rootB)?.order).toBe(1);
  });

  it('applies the canonical response placements, correcting a disagreeing optimistic guess', async () => {
    service.folders.set([
      folder({ id: rootA, order: 0 }),
      folder({ id: rootB, order: 1 }),
      folder({ id: rootC, order: 2 }),
    ]);
    api.moveFolder.and.resolveTo({
      success: true,
      folders: [
        { id: rootC, parentId: null, order: 0, isDefault: false },
        { id: rootA, parentId: null, order: 1, isDefault: false },
        { id: rootB, parentId: null, order: 2, isDefault: false },
      ],
    });

    await service.moveFolder({ folderId: rootC, targetId: rootB, position: 'after' });

    expect(service.getFolderById(rootC)?.order).toBe(0);
    expect(service.getFolderById(rootA)?.order).toBe(1);
    expect(service.getFolderById(rootB)?.order).toBe(2);
  });

  it("converges a FoldersReorderedEvent from another client, preserving isExpanded and widgets", () => {
    service.folders.set([
      folder({ id: rootA, order: 0, isExpanded: true, widgets: [widget('w1', rootA)] }),
      folder({ id: rootB, order: 1 }),
    ]);

    emitReordered({
      profileId: 'profile',
      folders: [
        { id: rootB, parentId: null, order: 0, isDefault: false },
        { id: rootA, parentId: null, order: 1, isDefault: true },
      ],
    });

    const a = service.getFolderById(rootA);
    expect(a?.order).toBe(1);
    expect(a?.isDefault).toBeTrue();
    expect(a?.isExpanded).toBeTrue();
    expect(a?.widgets).toEqual([widget('w1', rootA)]);
  });

  it('ignores a FoldersReorderedEvent from outside the active profile scope', async () => {
    api.getFolders.and.resolveTo({
      folders: [{
        id: rootA, name: rootA, profileId: 'profile', order: 0, rows: 3, columns: 5,
        isDefault: false, widgets: [],
      }],
    });
    await service.loadFolders('profile');

    emitReordered({ profileId: 'other-profile', folders: [{ id: rootA, parentId: null, order: 9, isDefault: true }] });

    expect(service.getFolderById(rootA)?.order).toBe(0);
    expect(service.getFolderById(rootA)?.isDefault).toBeFalse();
  });

  describe('resolveMove', () => {
    beforeEach(() => {
      service.folders.set([
        folder({ id: rootA, order: 0 }),
        folder({ id: rootB, order: 1 }),
        folder({ id: rootC, order: 2 }),
        folder({ id: childA1, order: 0, parentId: rootA }),
        folder({ id: childA2, order: 1, parentId: rootA }),
      ]);
    });

    it('up resolves to before the previous sibling', () => {
      expect(service.resolveMove(rootB, 'up')).toEqual({ folderId: rootB, targetId: rootA, position: 'before' });
    });

    it('up is null at the first sibling', () => {
      expect(service.resolveMove(rootA, 'up')).toBeNull();
    });

    it('down resolves to after the next sibling', () => {
      expect(service.resolveMove(rootA, 'down')).toEqual({ folderId: rootA, targetId: rootB, position: 'after' });
    });

    it('down is null at the last sibling', () => {
      expect(service.resolveMove(rootC, 'down')).toBeNull();
    });

    it('into resolves to inside the previous sibling', () => {
      expect(service.resolveMove(rootB, 'into')).toEqual({ folderId: rootB, targetId: rootA, position: 'inside' });
    });

    it('into is null on the first sibling', () => {
      expect(service.resolveMove(rootA, 'into')).toBeNull();
    });

    it('into resolves among nested siblings too', () => {
      expect(service.resolveMove(childA2, 'into')).toEqual({ folderId: childA2, targetId: childA1, position: 'inside' });
    });

    it('out resolves to after the former parent', () => {
      expect(service.resolveMove(childA1, 'out')).toEqual({ folderId: childA1, targetId: rootA, position: 'after' });
    });

    it('out is null on a root folder', () => {
      expect(service.resolveMove(rootA, 'out')).toBeNull();
    });

    it('is null for an unknown folder id', () => {
      expect(service.resolveMove('missing-id', 'up')).toBeNull();
    });
  });
});
