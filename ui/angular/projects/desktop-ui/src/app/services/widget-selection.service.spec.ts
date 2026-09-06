import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { Folder, GridWidget, WIDGET_GRID_VIEW_ID, WidgetType } from '@macro-deck/runtime';
import { ActionExecutionService, ApiService, FolderService, ProfileService } from '@shared';
import { WidgetSelectionService } from './widget-selection.service';

function widget(id: string, folderId: string, x: number, y: number, w = 1, h = 1, overrides: Partial<GridWidget> = {}): GridWidget {
  return { id, folderId, x, y, w, h, type: WidgetType.ActionButton, data: { label: id }, ...overrides };
}

describe('WidgetSelectionService', () => {
  let service: WidgetSelectionService;
  let folderService: FolderService;

  const w1 = widget('w1', 'f1', 0, 0);
  const w2 = widget('w2', 'f1', 3, 0);
  const wStraddle = widget('wStraddle', 'f1', 3, 0, 2, 1);
  const wOutside = widget('wOutside', 'f1', 10, 10);
  const wFar = widget('wFar', 'f1', 0, 10);
  const wPinnedLocal = widget('wPinnedLocal', 'f1', 5, 5, 1, 1, { isPinned: true, pinScope: 'Profile' });
  const wPinnedForeign = widget('wPinnedForeign', 'f2', 0, 0, 1, 1, { isPinned: true, pinScope: 'Profile' });

  function folders(): Folder[] {
    return [
      {
        id: 'f1', name: 'F1', parentId: null, order: 0, isExpanded: true, isDefault: true,
        cols: 20, rows: 20, background: '', spacing: null, borderRadius: null,
        viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null,
        widgets: [w1, w2, wStraddle, wOutside, wFar, wPinnedLocal],
      },
      {
        id: 'f2', name: 'F2', parentId: null, order: 1, isExpanded: true, isDefault: false,
        cols: 10, rows: 10, background: '', spacing: null, borderRadius: null,
        viewId: WIDGET_GRID_VIEW_ID, viewConfiguration: null,
        widgets: [wPinnedForeign],
      },
    ];
  }

  beforeEach(() => {
    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'reportFolderChanged', 'onWidgetTypeCatalogChanged']);
    apiSpy.onNotification.and.callFake(<T>(): Observable<T> => new Subject<unknown>().asObservable() as Observable<T>);
    apiSpy.onWidgetTypeCatalogChanged.and.returnValue(new Subject<never>().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal('disconnected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ApiService, useValue: apiSpy },
        { provide: ProfileService, useValue: { selectedProfileId: () => null, selectedProfile: () => null } },
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });

    folderService = TestBed.inject(FolderService);
    folderService.folders.set(folders());
    folderService.selectedFolderId.set('f1');
    TestBed.tick();

    service = TestBed.inject(WidgetSelectionService);
    TestBed.tick();
  });

  it('starts empty', () => {
    expect(service.ids().size).toBe(0);
    expect(service.hasSelection()).toBeFalse();
    expect(service.count()).toBe(0);
  });

  describe('toggle', () => {
    it('toggles a widget in and out without disturbing the rest of the selection', () => {
      service.toggle('w1');
      service.toggle('w2');
      expect([...service.ids()].sort()).toEqual(['w1', 'w2']);

      service.toggle('w1');
      expect([...service.ids()]).toEqual(['w2']);
    });

    it('can toggle down to an empty selection', () => {
      service.toggle('w1');
      service.toggle('w1');
      expect(service.hasSelection()).toBeFalse();
    });

    it('never adds a widget pinned in and displayed from another folder', () => {
      service.toggle('w1');
      service.toggle('wPinnedForeign');

      expect([...service.ids()]).toEqual(['w1']);
    });
  });

  describe('selectOnly', () => {
    it('replaces the selection with the clicked widget', () => {
      service.toggle('w1');
      service.selectOnly('w2');
      expect([...service.ids()]).toEqual(['w2']);
    });

    it('clears the selection rather than selecting a foreign pinned widget', () => {
      service.toggle('w1');
      service.selectOnly('wPinnedForeign');
      expect(service.hasSelection()).toBeFalse();
    });
  });

  describe('deselect', () => {
    it('removes only the named widget and leaves the rest selected', () => {
      service.selectRange(w1, w2, folderService.currentWidgets());
      const before = service.count();

      service.deselect('w2');

      expect(service.ids().has('w2')).toBeFalse();
      expect(service.ids().has('w1')).toBeTrue();
      expect(service.count()).toBe(before - 1);
    });

    it('does not move the range anchor', () => {
      service.selectOnly('w1');
      service.selectRange(w1, w2, folderService.currentWidgets());

      service.deselect('w2');

      expect(service.anchorWidgetId).toBe('w1');
    });

    it('is a no-op for a widget that is not selected', () => {
      service.selectOnly('w1');

      service.deselect('w2');

      expect([...service.ids()]).toEqual(['w1']);
    });
  });

  describe('selectRange', () => {
    it('selects every widget intersecting the bounding box of anchor and target, including one straddling the edge', () => {
      service.selectRange(w1, w2, folderService.currentWidgets());

      const ids = service.ids();
      expect(ids.has('w1')).toBeTrue();
      expect(ids.has('w2')).toBeTrue();
      expect(ids.has('wStraddle')).toBeTrue();
      expect(ids.has('wOutside')).toBeFalse();
    });

    it('re-derives from the same anchor on a successive range instead of growing cumulatively', () => {
      service.selectRange(w1, w2, folderService.currentWidgets());
      expect(service.ids().has('wFar')).toBeFalse();

      service.selectRange(w1, wFar, folderService.currentWidgets());

      const ids = service.ids();
      expect(ids.has('wFar')).toBeTrue();
      expect(ids.has('w1')).toBeTrue();
      expect(ids.has('w2')).toBeFalse();
      expect(ids.has('wStraddle')).toBeFalse();
    });

    it('never selects a widget pinned in and displayed from another folder', () => {
      service.selectRange(w1, wPinnedForeign, folderService.currentWidgets());
      expect(service.ids().has('wPinnedForeign')).toBeFalse();
    });

    it('does select a widget that is pinned but homed in the selected folder', () => {
      service.selectRange(w1, wPinnedLocal, folderService.currentWidgets());
      expect(service.ids().has('wPinnedLocal')).toBeTrue();
    });
  });

  describe('setMany (marquee)', () => {
    it('replaces the selection by default', () => {
      service.toggle('w1');
      service.setMany(['w2'], false);
      expect([...service.ids()]).toEqual(['w2']);
    });

    it('extends the selection when additive', () => {
      service.toggle('w1');
      service.setMany(['w2'], true);
      expect([...service.ids()].sort()).toEqual(['w1', 'w2']);
    });

    it('drops a foreign pinned widget id from the incoming set', () => {
      service.setMany(['w1', 'wPinnedForeign'], false);
      expect([...service.ids()]).toEqual(['w1']);
    });

    it('keeps a locally-homed pinned widget id from the incoming set', () => {
      service.setMany(['wPinnedLocal'], false);
      expect([...service.ids()]).toEqual(['wPinnedLocal']);
    });
  });

  describe('selectAll', () => {
    it('selects every widget homed in the selected folder, dropping foreign pinned widgets', () => {
      service.selectAll(folderService.currentWidgets());

      const ids = service.ids();
      expect(ids.has('w1')).toBeTrue();
      expect(ids.has('wPinnedLocal')).toBeTrue();
      expect(ids.has('wPinnedForeign')).toBeFalse();
    });
  });

  it('clears the selection when the selected folder changes', () => {
    service.toggle('w1');
    expect(service.hasSelection()).toBeTrue();

    folderService.selectedFolderId.set('f2');
    TestBed.tick();

    expect(service.hasSelection()).toBeFalse();
  });

  it('prunes an id that disappears from currentWidgets, leaving the rest of the selection intact', () => {
    service.toggle('w1');
    service.toggle('w2');

    folderService.folders.update(all =>
      all.map(f => f.id === 'f1' ? { ...f, widgets: f.widgets.filter(w => w.id !== 'w1') } : f));
    TestBed.tick();

    expect([...service.ids()]).toEqual(['w2']);
  });
});
