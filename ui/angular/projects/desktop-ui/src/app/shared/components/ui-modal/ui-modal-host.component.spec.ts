import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { EMPTY, Observable, Subject } from 'rxjs';

import { UiModalHostComponent } from './ui-modal-host.component';
import { ModalComponent } from '../overlay/modal/modal.component';
import {
  UiSessionHandle, UiSessionOpenRequest, UiSessionRejection, UiSessionService,
} from '../../services/ui-session.service';
import { ApiService, ConnectionState } from '../../transport';
import { HOST_URL_RESOLVER } from '../../transport/host-url';
import { provideLocalizationTesting } from '../../localization/localization-test-support';
import { type UiModalOpenedEvent, UiNode, UiNodeEvent } from '@macro-deck/runtime';

class FakeUiSessionHandle implements UiSessionHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(0);
  readonly rejection = signal<UiSessionRejection | null>(null);
  readonly fault = signal<UiSessionRejection | null>(null);
  readonly generation = signal(0);
  readonly reopenReason = signal<string | null>(null);
  closed = false;
  readonly sent: UiNodeEvent[] = [];

  send(event: UiNodeEvent): void {
    this.sent.push(event);
  }

  close(): void {
    this.closed = true;
  }
}

const TREE: UiNode = { id: 'root', type: 'Text', properties: { text: 'Pick a duration' } } as UiNode;

describe('UiModalHostComponent', () => {
  let opened: Subject<UiModalOpenedEvent>;
  let handles: FakeUiSessionHandle[];
  let api: jasmine.SpyObj<ApiService>;
  let fixture: ComponentFixture<UiModalHostComponent>;

  function html(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function notice(): HTMLElement | null {
    return html().querySelector('[role="status"]');
  }

  function openModal(): FakeUiSessionHandle {
    opened.next({ modalId: 'modal-1', title: 'Set countdown' as never });
    fixture.detectChanges();
    return handles[handles.length - 1];
  }

  beforeEach(() => {
    opened = new Subject<UiModalOpenedEvent>();
    handles = [];

    api = jasmine.createSpyObj<ApiService>('ApiService',
      ['getLocalization', 'onNotification', 'onUiModalOpened', 'completeUiModal']);
    api.getLocalization.and.resolveTo({
      culture: 'en', fallbackCulture: 'en', translations: {}, followSystem: false, availableCultures: ['en'],
    });
    api.onNotification.and.callFake(<T,>(): Observable<T> => EMPTY);
    api.onUiModalOpened.and.returnValue(opened.asObservable());
    api.completeUiModal.and.resolveTo({ accepted: true });
    Object.defineProperty(api, 'connectionStateSignal', { value: signal<ConnectionState>('connected') });

    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        {
          provide: UiSessionService,
          useValue: {
            open: (_request: UiSessionOpenRequest): UiSessionHandle => {
              const handle = new FakeUiSessionHandle();
              handles.push(handle);
              return handle;
            },
          },
        },
        { provide: ApiService, useValue: api },
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
      ],
    });

    fixture = TestBed.createComponent(UiModalHostComponent);
    fixture.detectChanges();
  });

  it('explains a dialog whose session was refused instead of showing an empty frame', () => {
    const handle = openModal();

    handle.rejection.set({ code: 'PROVIDER_UNAVAILABLE', message: 'No provider serves that id.' });
    fixture.detectChanges();

    expect(notice()?.textContent).toContain('This dialog cannot be shown');
    expect(notice()?.textContent).toContain('PROVIDER_UNAVAILABLE');
  });

  it('shows nothing but the dialog while its session is still opening', () => {
    openModal();

    expect(notice()).toBeNull();
  });

  it('shows the dialog content and no notice once a tree arrives', () => {
    const handle = openModal();

    handle.root.set(TREE);
    fixture.detectChanges();

    expect(notice()).toBeNull();
    expect(html().textContent).not.toContain('This dialog cannot be shown');
  });

  it('replaces content it was already showing when the session is lost', () => {
    const handle = openModal();
    handle.root.set(TREE);
    fixture.detectChanges();

    handle.reopenReason.set('PROVIDER_TIMEOUT');
    handle.rejection.set({ code: 'SESSION_NOT_FOUND' });
    fixture.detectChanges();

    expect(notice()?.textContent).toContain('This dialog cannot be shown');
    expect(notice()?.textContent).toContain('PROVIDER_TIMEOUT');
    expect(notice()?.textContent).not.toContain('SESSION_NOT_FOUND');
  });

  it('reports a session that faulted', () => {
    const handle = openModal();

    handle.fault.set({ code: 'PROVIDER_FAULTED' });
    fixture.detectChanges();

    expect(notice()?.textContent).toContain('PROVIDER_FAULTED');
  });

  it('still settles the waiting action as cancelled when the failed dialog is closed', () => {
    const handle = openModal();
    handle.rejection.set({ code: 'SESSION_NOT_FOUND' });
    fixture.detectChanges();

    fixture.debugElement.query(By.directive(ModalComponent)).componentInstance.close.emit();
    fixture.detectChanges();

    expect(api.completeUiModal).toHaveBeenCalledWith({ modalId: 'modal-1', cancelled: true, value: undefined });
    expect(handle.closed).toBeTrue();
    expect(notice()).toBeNull();
  });
});
