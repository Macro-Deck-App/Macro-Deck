import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PluginSessionInfo } from '@macro-deck/runtime';
import { ToastService } from '@shared';
import { DeveloperModeService } from '../../../../../../services/developer-mode.service';
import { PluginTokenService } from '../../../../../../services/plugin-token.service';
import { SessionsSectionComponent } from './sessions-section.component';
import { provideLocalizationTesting } from '../../../../../../../testing/localization-test-support';

function session(id: string, overrides: Partial<PluginSessionInfo> = {}): PluginSessionInfo {
  return {
    sessionId: id,
    pluginId: `plugin-${id}`,
    tokenId: 't1',
    origin: 'self-registered',
    negotiatedVersion: 1,
    state: 'connected',
    connectedAt: '2026-08-01T00:00:00Z',
    lastSeenAt: '2026-08-01T00:00:00Z',
    ...overrides,
  };
}

describe('SessionsSectionComponent', () => {
  let fixture: ComponentFixture<SessionsSectionComponent>;
  let serviceSpy: jasmine.SpyObj<PluginTokenService>;
  let sessionsSignal: WritableSignal<PluginSessionInfo[]>;
  let developerModeEnabled: WritableSignal<boolean>;

  function configure(sessions: PluginSessionInfo[] = []): void {
    serviceSpy = jasmine.createSpyObj<PluginTokenService>('PluginTokenService', ['load', 'terminateSession']);
    sessionsSignal = signal(sessions);
    Object.defineProperty(serviceSpy, 'tokens', { value: signal([]) });
    Object.defineProperty(serviceSpy, 'sessions', { value: sessionsSignal });
    Object.defineProperty(serviceSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(serviceSpy, 'loadError', { value: signal<string | null>(null) });
    serviceSpy.load.and.resolveTo();
    serviceSpy.terminateSession.and.resolveTo({ success: true });

    const developerModeSpy = jasmine.createSpyObj<DeveloperModeService>('DeveloperModeService', ['ensureLoaded']);
    developerModeEnabled = signal(true);
    Object.defineProperty(developerModeSpy, 'enabled', { value: developerModeEnabled });
    developerModeSpy.ensureLoaded.and.resolveTo();

    TestBed.configureTestingModule({
      imports: [SessionsSectionComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: PluginTokenService, useValue: serviceSpy },
        { provide: DeveloperModeService, useValue: developerModeSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<SessionsSectionComponent>> {
    const f = TestBed.createComponent(SessionsSectionComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  it('shows the empty state when no plugin the developer runs is connected', async () => {
    configure();
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.plugin-tokens-empty')).toBeTruthy();
  });

  // A host-managed session also has `tokenId === null`, so only `origin` separates the two.
  it('lists self-registered sessions and leaves host-managed ones to the Managed plugins section', async () => {
    configure([
      session('s1', { origin: 'self-registered', displayName: 'Example Tools' }),
      session('s2', { origin: 'managed', tokenId: null, displayName: 'Stream Deck' }),
    ]);
    fixture = await create();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Example Tools');
    expect(text).not.toContain('Stream Deck');
    expect(fixture.nativeElement.querySelectorAll('.plugin-tokens-row').length).toBe(1);
  });

  it('orders sessions newest first', async () => {
    configure([
      session('older', { displayName: 'Older', connectedAt: '2026-08-01T00:00:00Z' }),
      session('newer', { displayName: 'Newer', connectedAt: '2026-08-05T00:00:00Z' }),
    ]);
    fixture = await create();

    expect(fixture.componentInstance.sessions().map(s => s.sessionId)).toEqual(['newer', 'older']);
  });

  it('falls back to the plugin id when the session carries no display name', async () => {
    configure([session('s1', { pluginId: 'com.example.tools', displayName: null })]);
    fixture = await create();

    expect((fixture.nativeElement.querySelector('.plugin-tokens-name') as HTMLElement).textContent)
      .toContain('com.example.tools');
  });

  it('shows the negotiated protocol version', async () => {
    configure([session('s1', { negotiatedVersion: 3 })]);
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.plugin-tokens-stats').textContent).toContain('3');
  });

  it('ends the session through the service on confirm', async () => {
    configure([session('s1')]);
    fixture = await create();

    fixture.componentInstance.requestTerminate(fixture.componentInstance.sessions()[0]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();

    await fixture.componentInstance.confirmTerminate();

    expect(serviceSpy.terminateSession).toHaveBeenCalledOnceWith('s1');
  });

  it('toasts the host message when ending the session fails', async () => {
    configure([session('s1')]);
    fixture = await create();
    const toastSpy = spyOn(TestBed.inject(ToastService), 'show');
    serviceSpy.terminateSession.and.resolveTo({
      success: false, error: { code: 'not_found', message: 'That session is already gone.' },
    });

    fixture.componentInstance.requestTerminate(fixture.componentInstance.sessions()[0]);
    await fixture.componentInstance.confirmTerminate();

    expect(toastSpy).toHaveBeenCalledWith('That session is already gone.', { variant: 'error' });
  });
});
