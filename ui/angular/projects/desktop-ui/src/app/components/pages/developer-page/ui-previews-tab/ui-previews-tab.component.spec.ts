import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
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

  send(): void {}

  close(): void {}
}

function fakeUiSessionService(): UiSessionService {
  return { open: (): UiSessionHandle => new FakeUiSessionHandle() } as unknown as UiSessionService;
}

describe('UiPreviewsTabComponent', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let fixture: ComponentFixture<UiPreviewsTabComponent>;

  async function setUp(response: ListUiPreviewsResponse): Promise<void> {
    apiSpy = jasmine.createSpyObj<ApiService>('ApiService', ['listUiPreviews', 'onNotification']);
    apiSpy.listUiPreviews.and.resolveTo(response);
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal<ConnectionState>('connected') });

    TestBed.configureTestingModule({
      imports: [UiPreviewsTabComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: ApiService, useValue: apiSpy },
        { provide: UiSessionService, useFactory: fakeUiSessionService },
      ],
    });

    fixture = TestBed.createComponent(UiPreviewsTabComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
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
    apiSpy.onNotification.and.returnValue(new Subject().asObservable());
    Object.defineProperty(apiSpy, 'connectionStateSignal', { value: signal<ConnectionState>('connected') });

    TestBed.configureTestingModule({
      imports: [UiPreviewsTabComponent],
      providers: [
        provideZonelessChangeDetection(),
        provideLocalizationTesting(),
        { provide: ApiService, useValue: apiSpy },
        { provide: UiSessionService, useFactory: fakeUiSessionService },
      ],
    });

    fixture = TestBed.createComponent(UiPreviewsTabComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

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
});
