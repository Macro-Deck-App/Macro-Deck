import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';

import { UiNode } from '@macro-deck/runtime';
import { ApiService, ConnectionState, HOST_URL_RESOLVER, UiSessionOpenRequestSource, UiSessionRejection, UiSessionService } from '@shared';
import { IntegrationUiPagePanelComponent } from './integration-ui-page-panel.component';

class FakeHandle {
  readonly root = signal<UiNode | null>(null);
  readonly revision = signal(1);
  readonly rejection = signal<UiSessionRejection | null>(null);
  readonly fault = signal<UiSessionRejection | null>(null);
  readonly generation = signal(1);
  readonly send = jasmine.createSpy('send');
  readonly close = jasmine.createSpy('close');
  readonly uploadContent = jasmine.createSpy('uploadContent').and.resolveTo(null);

  constructor(readonly request: UiSessionOpenRequestSource) {}
}

describe('IntegrationUiPagePanelComponent', () => {
  let handles: FakeHandle[];
  let connection: ReturnType<typeof signal<ConnectionState>>;

  beforeEach(() => {
    handles = [];
    connection = signal<ConnectionState>('connected');
    TestBed.configureTestingModule({
      imports: [IntegrationUiPagePanelComponent],
      providers: [
        provideZonelessChangeDetection(),
        { provide: HOST_URL_RESOLVER, useValue: async () => 'http://host' },
        {
          provide: UiSessionService,
          useValue: {
            open: (request: UiSessionOpenRequestSource) => {
              const handle = new FakeHandle(request);
              handles.push(handle);
              return handle;
            },
          },
        },
        {
          provide: ApiService,
          useValue: {
            connectionStateSignal: connection,
            getLocalization: async () => ({ culture: 'en', fallbackCulture: 'en', translations: {}, availableCultures: ['en'] }),
            onNotification: () => EMPTY,
          },
        },
      ],
    });
  });

  async function render(available = true): Promise<ComponentFixture<IntegrationUiPagePanelComponent>> {
    const fixture = TestBed.createComponent(IntegrationUiPagePanelComponent);
    fixture.componentRef.setInput('integrationId', 'app.example');
    fixture.componentRef.setInput('pageId', 'scripts');
    fixture.componentRef.setInput('available', available);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('opens the page session when shown, renders its tree and closes it when left', async () => {
    const fixture = await render();

    expect(handles.length).toBe(1);
    expect(handles[0].request).toEqual({ kind: 'page', integrationId: 'app.example', pageId: 'scripts' });

    handles[0].root.set({ id: 'title', type: 'heading', properties: { text: 'My scripts' } });
    fixture.detectChanges();
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('shared-ui-tree')).not.toBeNull();

    fixture.destroy();
    expect(handles[0].close).toHaveBeenCalled();
  });

  it('opens nothing while the integration cannot serve its page', async () => {
    await render(false);

    expect(handles).toEqual([]);
  });

  it('offers to try again after the page was refused, and opens a new session for it', async () => {
    const fixture = await render();
    handles[0].rejection.set({ code: 'provider_unavailable', message: 'Not ready yet' });
    fixture.detectChanges();
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Not ready yet');
    (element.querySelector('shared-empty-state shared-button button') as HTMLButtonElement).click();

    expect(handles.length).toBe(2);
    expect(handles[0].close).toHaveBeenCalled();
  });

  it('keeps the page session open while the connection to the host drops and comes back', async () => {
    const fixture = await render();

    connection.set('reconnecting');
    fixture.detectChanges();
    await fixture.whenStable();
    connection.set('connected');
    fixture.detectChanges();
    await fixture.whenStable();

    expect(handles.length).toBe(1);
    expect(handles[0].close).not.toHaveBeenCalled();
  });
});
