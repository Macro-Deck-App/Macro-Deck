import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { ListUiPreviewsResponse, UiPreviewEntry } from '@macro-deck/runtime';
import { ApiService, ConnectionState, UiSessionHandle, UiSessionService } from '@shared';
import { IntegrationService } from '../../../../services/integration.service';
import { provideLocalizationTesting } from '../../../../../testing/localization-test-support';
import { PreviewCanvasComponent } from './preview-canvas.component';
import { UiPreviewsTabComponent } from './ui-previews-tab.component';

function preview(id: string, view: string, scenario: string, ownerId = ''): UiPreviewEntry {
  return { id, view, scenario, profile: 'widget', ownerId };
}

class FakeUiSessionHandle implements UiSessionHandle {
  readonly root = signal(null);
  readonly revision = signal(0);
  readonly rejection = signal(null);
  readonly generation = signal(0);

  send(): void {}

  close(): void {}
}

function fakeUiSessionService(): UiSessionService {
  return { open: (): UiSessionHandle => new FakeUiSessionHandle() } as unknown as UiSessionService;
}

describe('UiPreviewsTabComponent', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let integrationsChanged: Subject<unknown>;
  let fixture: ComponentFixture<UiPreviewsTabComponent>;

  function configure(queryParams: Record<string, string> = {}): void {
    integrationsChanged = new Subject();
    apiSpy.onNotification.and.callFake(<T>(method: string) =>
      (method === 'IntegrationsChangedEvent' ? integrationsChanged : new Subject<T>()).asObservable() as never);
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal<ConnectionState>('connected') });
    routerSpy = jasmine.createSpyObj<Router>('Router', ['navigate']);
    routerSpy.navigate.and.resolveTo(true);

    TestBed.configureTestingModule({
      imports: [UiPreviewsTabComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: ApiService, useValue: apiSpy },
        { provide: UiSessionService, useFactory: fakeUiSessionService },
        { provide: Router, useValue: routerSpy },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } } },
      ],
    });
  }

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(UiPreviewsTabComponent);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(response: ListUiPreviewsResponse, queryParams: Record<string, string> = {}): Promise<void> {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['listUiPreviews', 'onNotification']);
    apiSpy.listUiPreviews.and.resolveTo(response);
    configure(queryParams);
    await render();
  }

  function canvas(): PreviewCanvasComponent | null {
    return fixture.debugElement.query(By.directive(PreviewCanvasComponent))?.componentInstance ?? null;
  }

  async function integrationChangeSettled(): Promise<void> {
    const before = apiSpy.listUiPreviews.calls.count();
    integrationsChanged.next({ integrationId: 'plugin.example' });
    await waitFor(() => apiSpy.listUiPreviews.calls.count() > before);
    await settle();
  }

  async function waitFor(condition: () => boolean): Promise<void> {
    const deadline = Date.now() + 5000;
    while (!condition()) {
      if (Date.now() > deadline) throw new Error('The preview list was never reloaded.');
      await new Promise(resolve => setTimeout(resolve, 20));
    }
  }

  function groupHeaders(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.group-view'))
      .map(el => el.textContent?.trim() ?? '');
  }

  function scenarioLabels(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.rail-item-name'))
      .map(el => el.textContent?.trim() ?? '');
  }

  it('groups scenarios under their view, and keeps two owners of the same view separate', async () => {
    await setUp({
      previews: [
        preview('a1', 'MainView', 'Empty', ''),
        preview('a2', 'MainView', 'WithData', ''),
        preview('b1', 'DetailView', 'Default', ''),
        preview('c1', 'MainView', 'PluginScenario', 'plugin.example'),
      ],
      diagnostics: [],
    });

    // Four previews, but only three (owner, view) groups: built-in MainView, built-in DetailView,
    // and plugin.example's own MainView - which must not merge with the built-in one of the same name.
    const headers = groupHeaders();
    expect(headers.filter(h => h === 'MainView').length).toBe(2);
    expect(headers.filter(h => h === 'DetailView').length).toBe(1);

    const labels = scenarioLabels();
    expect(labels).toContain('Empty');
    expect(labels).toContain('WithData');
    expect(labels).toContain('Default');
    expect(labels).toContain('PluginScenario');
  });

  it('shows the empty state when nothing is registered', async () => {
    await setUp({ previews: [], diagnostics: [] });

    expect((fixture.nativeElement as HTMLElement).querySelector('shared-empty-state')).toBeTruthy();
  });

  it('shows skipped diagnostics alongside the list', async () => {
    await setUp({
      previews: [preview('a1', 'MainView', 'Empty')],
      diagnostics: [{ member: 'BadPreview.Method', reason: 'threw an exception' }],
    });

    const text = (fixture.nativeElement as HTMLElement).querySelector('.diagnostics')?.textContent ?? '';
    expect(text).toContain('BadPreview.Method');
    expect(text).toContain('threw an exception');
  });

  it('mounts the canvas for the selected preview and shows the no-selection state otherwise', async () => {
    await setUp({ previews: [preview('a1', 'MainView', 'Empty')], diagnostics: [] });

    expect(fixture.debugElement.query(By.directive(PreviewCanvasComponent))).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No preview selected');

    const railButton = (fixture.nativeElement as HTMLElement).querySelector('.rail-item') as HTMLButtonElement;
    railButton.click();
    fixture.detectChanges();
    await fixture.whenStable();

    const canvas = fixture.debugElement.query(By.directive(PreviewCanvasComponent));
    expect(canvas).toBeTruthy();
    expect((canvas.componentInstance as PreviewCanvasComponent).preview()).toEqual(preview('a1', 'MainView', 'Empty'));
  });

  it('reports a load failure', async () => {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['listUiPreviews', 'onNotification']);
    apiSpy.listUiPreviews.and.rejectWith(new Error('offline'));
    configure();
    await render();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('could not be loaded');
  });

  it('resolves a plugin owner label from the integration service when available', async () => {
    await setUp({
      previews: [preview('a1', 'MainView', 'Empty', 'plugin.example')],
      diagnostics: [],
    });

    TestBed.inject(IntegrationService).integrations.set([{
      id: 'plugin.example', name: 'Example Plugin', version: '1.0.0', isInternal: false, enabled: true,
      actionCount: 0, variableCount: 0, supportsConfigFlow: false, allowsMultipleConfigurations: false,
      configuredEntryCount: 0, hasIcon: false, iconVersion: null, issueCount: 0, issueSeverity: null,
      isInitialized: true, variablesDependOnConfiguration: false, providedCapabilities: [],
    }]);
    fixture.detectChanges();

    const ownerLabels = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.group-owner'))
      .map(el => el.textContent?.trim());
    expect(ownerLabels).toContain('Example Plugin');
  });
  describe('across plugin restarts and reloads', () => {
    const scenario = preview('p1', 'MainView', 'Default', 'plugin.example');
    const sibling = preview('p2', 'MainView', 'Other', 'plugin.example');

    it('reloads the list on an integration change and keeps the selection, reporting availability', async () => {
      await setUp({ previews: [scenario, sibling], diagnostics: [] }, { preview: 'p1', owner: 'plugin.example' });
      expect(canvas()?.available()).toBeTrue();
      const revision = canvas()!.catalogRevision();

      apiSpy.listUiPreviews.and.resolveTo({ previews: [], diagnostics: [] });
      await integrationChangeSettled();

      expect(canvas()?.preview()?.id).toBe('p1');
      expect(canvas()?.available()).toBeFalse();
      expect(canvas()?.ownerConnected()).toBeFalse();

      apiSpy.listUiPreviews.and.resolveTo({ previews: [scenario, sibling], diagnostics: [] });
      await integrationChangeSettled();

      expect(canvas()?.available()).toBeTrue();
      expect(canvas()!.catalogRevision()).toBe(revision + 2);
    });

    it('treats a scenario its connected plugin stopped listing as gone rather than waiting', async () => {
      await setUp({ previews: [scenario, sibling], diagnostics: [] }, { preview: 'p1', owner: 'plugin.example' });

      apiSpy.listUiPreviews.and.resolveTo({ previews: [sibling], diagnostics: [] });
      await integrationChangeSettled();

      expect(canvas()?.available()).toBeFalse();
      expect(canvas()?.ownerConnected()).toBeTrue();
    });

    it('fetches again when a change arrives while a fetch is still running', async () => {
      await setUp({ previews: [scenario], diagnostics: [] });
      let release!: () => void;
      apiSpy.listUiPreviews.and.returnValue(new Promise(resolve => {
        release = () => resolve({ previews: [], diagnostics: [] });
      }));
      const callsBefore = apiSpy.listUiPreviews.calls.count();
      integrationsChanged.next({});
      await waitFor(() => apiSpy.listUiPreviews.calls.count() > callsBefore);
      const callsWhileRunning = apiSpy.listUiPreviews.calls.count();

      apiSpy.listUiPreviews.and.resolveTo({ previews: [scenario, sibling], diagnostics: [] });
      integrationsChanged.next({});
      await new Promise(resolve => setTimeout(resolve, 1000));
      expect(apiSpy.listUiPreviews.calls.count()).toBe(callsWhileRunning);

      release();
      await waitFor(() => apiSpy.listUiPreviews.calls.count() === callsWhileRunning + 1);
      await settle();
      await fixture.whenStable();
      await settle();
      expect(scenarioLabels()).toContain('Other');
    });

    it('restores the selection, owner and size from the URL before the plugin has connected', async () => {
      await setUp({ previews: [], diagnostics: [] }, { preview: 'p1', owner: 'plugin.example', w: '320', h: '99999' });

      expect(canvas()?.preview()?.id).toBe('p1');
      expect(canvas()?.preview()?.ownerId).toBe('plugin.example');
      expect(canvas()?.available()).toBeFalse();
      expect(canvas()?.ownerConnected()).toBeFalse();
      expect(canvas()?.initialSize()).toEqual({ width: 320, height: 4000 });
    });

    it('ignores a size in the URL that is not a number', async () => {
      await setUp({ previews: [scenario], diagnostics: [] }, { preview: 'p1', w: 'wide', h: '200' });

      expect(canvas()?.initialSize()).toBeNull();
    });

    it('writes the selected scenario and its owner to the URL without adding history', async () => {
      await setUp({ previews: [scenario], diagnostics: [] });

      ((fixture.nativeElement as HTMLElement).querySelector('.rail-item') as HTMLButtonElement).click();
      await settle();

      expect(routerSpy.navigate).toHaveBeenCalledWith([], jasmine.objectContaining({
        queryParams: { preview: 'p1', owner: 'plugin.example' },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      }));
    });
  });
});
