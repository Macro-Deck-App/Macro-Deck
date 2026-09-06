import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';

import { ApiService } from '../transport';
import {
  ActionButtonData,
  Folder,
  GridWidget,
  WIDGET_GRID_VIEW_ID,
  WidgetType,
  type WidgetUpdatedEvent,
} from '@macro-deck/runtime';
import { ActionExecutionService } from './action-execution.service';
import { FolderService } from './folder.service';
import { ProfileService } from './profile.service';

describe('FolderService widget change propagation', () => {
  const folderId = '11111111-1111-1111-1111-111111111111';
  const widgetId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

  let apiSpy: jasmine.SpyObj<ApiService>;
  let notifications: Map<string, Subject<unknown>>;
  let service: FolderService;

  function folder(widgets: GridWidget[]): Folder {
    return {
      id: folderId,
      name: 'Home',
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

  function widgetUpdated(label?: string, id = widgetId): WidgetUpdatedEvent {
    return {
      folderId,
      widget: {
        id,
        type: WidgetType.ActionButton,
        positionX: 0,
        positionY: 0,
        width: 1,
        height: 1,
        data: label === undefined ? undefined : JSON.stringify({ mode: 'momentary', label }),
      },
    };
  }

  function storedLabel(): string | undefined {
    return (service.getFolderById(folderId)!.widgets[0].data as ActionButtonData).label;
  }

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['onNotification', 'reportFolderChanged', 'onWidgetTypeCatalogChanged']);
    notifications = new Map();
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
        { provide: ActionExecutionService, useValue: { waitFor: () => Promise.resolve(null) } },
      ],
    });

    service = TestBed.inject(FolderService);
    service.folders.set([folder([{
      id: widgetId,
      folderId,
      x: 0,
      y: 0,
      w: 1,
      h: 1,
      type: WidgetType.ActionButton,
      data: { label: 'configured' },
    }])]);
    service.selectedFolderId.set(folderId);
  });

  it('applies a pushed change to the widget every client renders', () => {
    notifications.get('WidgetUpdatedEvent')!.next(widgetUpdated('from a flow'));

    expect(storedLabel()).toBe('from a flow');
  });

  it('keeps the latest of two pushes in a row', () => {
    notifications.get('WidgetUpdatedEvent')!.next(widgetUpdated('first'));
    notifications.get('WidgetUpdatedEvent')!.next(widgetUpdated('second'));

    expect(storedLabel()).toBe('second');
  });

  it('leaves the local data alone when a push carries none', () => {
    notifications.get('WidgetUpdatedEvent')!.next(widgetUpdated('from a flow'));
    notifications.get('WidgetUpdatedEvent')!.next(widgetUpdated());

    expect(storedLabel()).toBe('from a flow');
  });

  it('ignores a push for a widget it does not have', () => {
    notifications.get('WidgetUpdatedEvent')!.next(
      widgetUpdated('somewhere else', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'));

    expect(storedLabel()).toBe('configured');
  });
});
