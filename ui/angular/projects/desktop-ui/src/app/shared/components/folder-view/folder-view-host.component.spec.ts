import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { EMPTY, Observable } from 'rxjs';

import { FolderViewHostComponent } from './folder-view-host.component';
import {
  UiSessionHandle, UiSessionOpenRequest, UiSessionRejection, UiSessionService,
} from '../../services/ui-session.service';
import { FolderViewService } from '../../services/folder-view.service';
import { ApiService, ConnectionState } from '../../transport';
import { HOST_URL_RESOLVER } from '../../transport/host-url';
import { provideLocalizationTesting } from '../../localization/localization-test-support';
import { type IpcFolderView, UiNode, UiNodeEvent } from '@macro-deck/runtime';

class FakeUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);
  closed = false;
  readonly sent: UiNodeEvent[] = [];

  send(event: UiNodeEvent): void {
    this.sent.push(event);
  }

  close(): void {
    this.closed = true;
  }
}

function view(id: string, navigation: string): IpcFolderView {
  return {
    id, providerId: 'com.example.home', name: 'Dashboard', navigation,
    hasConfiguration: false, isBuiltIn: false,
  };
}

describe('FolderViewHostComponent', () => {
  const viewId = 'com.example.home::dashboard';

  let opens: UiSessionOpenRequest[];
  let handles: FakeUiSessionHandle[];
  let catalog: IpcFolderView[];

  function createFixture(inputs: {
    folderId?: string;
    viewId?: string;
    canGoBack?: boolean;
    showRecoveryActions?: boolean;
  } = {}): ComponentFixture<FolderViewHostComponent> {
    const fixture = TestBed.createComponent(FolderViewHostComponent);
    fixture.componentRef.setInput('folderId', inputs.folderId ?? 'folder-1');
    fixture.componentRef.setInput('viewId', inputs.viewId ?? viewId);
    fixture.componentRef.setInput('canGoBack', inputs.canGoBack ?? false);
    if (inputs.showRecoveryActions !== undefined) {
      fixture.componentRef.setInput('showRecoveryActions', inputs.showRecoveryActions);
    }
    fixture.detectChanges();
    return fixture;
  }

  function html(fixture: ComponentFixture<FolderViewHostComponent>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    opens = [];
    handles = [];
    catalog = [view(viewId, 'default')];

    const fakeUiSessions = {
      open: (request: UiSessionOpenRequest): UiSessionHandle => {
        opens.push(request);
        const handle = new FakeUiSessionHandle();
        handles.push(handle);
        return handle;
      },
    };

    const apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['getLocalization', 'onNotification']);
    apiSpy.getLocalization.and.resolveTo({
      culture: 'en', fallbackCulture: 'en', translations: {}, followSystem: false, availableCultures: ['en'],
    });
    apiSpy.onNotification.and.callFake(<T,>(): Observable<T> => EMPTY);
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal<ConnectionState>('connected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: UiSessionService, useValue: fakeUiSessions },
        { provide: ApiService, useValue: apiSpy },
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
        {
          provide: FolderViewService,
          useValue: {
            folderViews: signal(catalog),
            load: () => Promise.resolve(),
            reload: () => Promise.resolve(),
            find: (id: string) => catalog.find(candidate => candidate.id === id),
          },
        },
      ],
    });
  });

  it('opens a folder session for the folder it is given', () => {
    createFixture({ folderId: 'folder-7' });

    expect(opens).toEqual([{ kind: 'folder', folderId: 'folder-7' }]);
  });

  describe('the back button', () => {
    it('is shown when the view asks for it and there is somewhere to go', () => {
      const fixture = createFixture({ canGoBack: true });

      expect(html(fixture).querySelector('.folder-view-back')).not.toBeNull();
    });

    // The button is Macro Deck's, but the target is the client's: with nothing to go back to it would
    // be a control that does nothing.
    it('is omitted at a root view even though the provider asked for it', () => {
      const fixture = createFixture({ canGoBack: false });

      expect(html(fixture).querySelector('.folder-view-back')).toBeNull();
    });

    it('is omitted for a view that provides its own way out', () => {
      catalog = [view(viewId, 'hidden')];
      const fixture = createFixture({ canGoBack: true });

      expect(html(fixture).querySelector('.folder-view-back')).toBeNull();
    });

    it('emits back rather than telling the provider', () => {
      const fixture = createFixture({ canGoBack: true });
      const back = jasmine.createSpy('back');
      fixture.componentInstance.back.subscribe(back);

      html(fixture).querySelector<HTMLButtonElement>('.folder-view-back')!.click();

      expect(back).toHaveBeenCalledTimes(1);
      expect(handles[0].sent).toEqual([]);
    });
  });

  describe('when nothing provides the view', () => {
    function rejectAndDetect(fixture: ComponentFixture<FolderViewHostComponent>): void {
      handles[0].rejection.set({ code: 'PROVIDER_UNAVAILABLE' });
      fixture.detectChanges();
    }

    it('shows the placeholder rather than an empty view', () => {
      const fixture = createFixture();
      rejectAndDetect(fixture);

      expect(html(fixture).querySelector('.folder-view-placeholder')).not.toBeNull();
    });

    // Both states render no tree, and telling a user their folder is broken while it is merely loading
    // would be worse than a moment of blank.
    it('does not show the placeholder while the session is still opening', () => {
      const fixture = createFixture();

      expect(html(fixture).querySelector('.folder-view-placeholder')).toBeNull();
    });

    it('offers a way out even for a view that asked to hide the back button', () => {
      catalog = [view(viewId, 'hidden')];
      const fixture = createFixture({ canGoBack: true });
      rejectAndDetect(fixture);

      expect(html(fixture).querySelector('.folder-view-back')).not.toBeNull();
    });

    it('offers no recovery actions on a client that cannot act on them', () => {
      const fixture = createFixture({ showRecoveryActions: false });
      rejectAndDetect(fixture);

      expect(html(fixture).querySelector('.folder-view-placeholder')).not.toBeNull();
      expect(html(fixture).querySelector('.folder-view-placeholder-actions')).toBeNull();
    });
  });

  it('closes the session when the folder changes', () => {
    const fixture = createFixture({ folderId: 'folder-1' });

    fixture.componentRef.setInput('folderId', 'folder-2');
    fixture.detectChanges();

    expect(handles[0].closed).toBeTrue();
    expect(opens.length).toBe(2);
  });
});
