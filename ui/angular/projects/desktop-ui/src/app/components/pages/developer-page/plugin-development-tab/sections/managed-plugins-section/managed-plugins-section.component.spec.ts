import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PairedPlugin, PluginRuntimeInfo } from '@macro-deck/runtime';
import { ToastService } from '@shared';
import { DeveloperModeService } from '../../../../../../services/developer-mode.service';
import { PluginPairingService } from '../../../../../../services/plugin-pairing.service';
import { PluginRuntimeService } from '../../../../../../services/plugin-runtime.service';
import { ManagedPluginsSectionComponent } from './managed-plugins-section.component';
import { provideLocalizationTesting } from '../../../../../../../testing/localization-test-support';

function plugin(pluginId: string, overrides: Partial<PluginRuntimeInfo> = {}): PluginRuntimeInfo {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    version: '1.0.0',
    state: 'running',
    health: 'healthy',
    managed: true,
    lastStopReason: 'none',
    consecutiveHealthFailures: 0,
    restartCount: 0,
    bootstrapOutput: [],
    ...overrides,
  };
}

function pairedPlugin(pluginId: string, overrides: Partial<PairedPlugin> = {}): PairedPlugin {
  return {
    pluginId,
    displayName: `Plugin ${pluginId}`,
    createdAt: '2026-08-01T00:00:00Z',
    lastSeenAt: null,
    online: false,
    ...overrides,
  };
}

describe('ManagedPluginsSectionComponent', () => {
  let fixture: ComponentFixture<ManagedPluginsSectionComponent>;
  let runtimeSpy: jasmine.SpyObj<PluginRuntimeService>;
  let pairingSpy: jasmine.SpyObj<PluginPairingService>;
  let pluginsSignal: WritableSignal<PluginRuntimeInfo[]>;
  let loadErrorSignal: WritableSignal<string | null>;
  let pairedPluginsSignal: WritableSignal<PairedPlugin[]>;
  let developerModeSignal: WritableSignal<boolean>;

  function configure(
    plugins: PluginRuntimeInfo[] = [],
    pairedPlugins: PairedPlugin[] = [],
    options: { pairingUnavailable?: boolean; developerMode?: boolean } = {},
  ): void {
    runtimeSpy = jasmine.createSpyObj<PluginRuntimeService>(
      'PluginRuntimeService', ['load', 'start', 'stop', 'restart']);
    pluginsSignal = signal(plugins);
    loadErrorSignal = signal<string | null>(null);
    Object.defineProperty(runtimeSpy, 'plugins', { value: pluginsSignal });
    Object.defineProperty(runtimeSpy, 'busy', { value: signal<ReadonlySet<string>>(new Set()) });
    Object.defineProperty(runtimeSpy, 'isLoading', { value: signal(false) });
    Object.defineProperty(runtimeSpy, 'loadError', { value: loadErrorSignal });
    runtimeSpy.load.and.resolveTo();
    runtimeSpy.start.and.resolveTo({ success: true });
    runtimeSpy.stop.and.resolveTo({ success: true });
    runtimeSpy.restart.and.resolveTo({ success: true });

    pairingSpy = jasmine.createSpyObj<PluginPairingService>('PluginPairingService', ['load', 'revoke']);
    pairedPluginsSignal = signal(pairedPlugins);
    Object.defineProperty(pairingSpy, 'pairedPlugins', { value: pairedPluginsSignal });
    Object.defineProperty(pairingSpy, 'loadError', { value: signal<string | null>(null) });
    Object.defineProperty(pairingSpy, 'unavailable', { value: signal(options.pairingUnavailable ?? false) });
    pairingSpy.load.and.resolveTo();
    pairingSpy.revoke.and.resolveTo();

    const developerModeSpy = jasmine.createSpyObj<DeveloperModeService>('DeveloperModeService', ['ensureLoaded']);
    developerModeSignal = signal(options.developerMode ?? true);
    Object.defineProperty(developerModeSpy, 'enabled', { value: developerModeSignal });
    developerModeSpy.ensureLoaded.and.resolveTo();

    TestBed.configureTestingModule({
      imports: [ManagedPluginsSectionComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: PluginRuntimeService, useValue: runtimeSpy },
        { provide: PluginPairingService, useValue: pairingSpy },
        { provide: DeveloperModeService, useValue: developerModeSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<ManagedPluginsSectionComponent>> {
    const f = TestBed.createComponent(ManagedPluginsSectionComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  it('shows the empty state when there are no supervised plugins', async () => {
    configure();
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.managed-plugins-empty')).toBeTruthy();
  });

  // Issue #709: the empty state named `package`, which the shared icon set has no mask for, so it
  // painted a plain filled square. `puzzle` is the plugin glyph the rest of the app uses.
  it('shows the plugin glyph in the empty state', async () => {
    configure();
    fixture = await create();

    const icon = fixture.nativeElement.querySelector('.managed-plugins-empty .es-icon') as HTMLElement;
    expect(icon.classList.contains('icon-puzzle')).toBeTrue();
  });

  it('renders one row per plugin with its state and health badges', async () => {
    configure([plugin('p1', { state: 'backoff', health: 'degraded' })]);
    fixture = await create();

    const row = fixture.nativeElement.querySelector('.managed-plugins-row');
    expect(row).toBeTruthy();
    expect(row.querySelector('.state-backoff')).toBeTruthy();
    expect(row.querySelector('.health-degraded')).toBeTruthy();
  });

  async function openMenu(pluginId: string): Promise<HTMLButtonElement[]> {
    fixture.componentInstance.setMenuOpen(pluginId, true);
    fixture.detectChanges();
    await fixture.whenStable();
    return Array.from(fixture.nativeElement.querySelectorAll('.managed-plugins-menu-item'));
  }

  it('disables Start while running and Stop while stopped', async () => {
    configure([plugin('p1', { state: 'running' }), plugin('p2', { state: 'stopped' })]);
    fixture = await create();

    const [startWhileRunning] = await openMenu('p1');
    expect(startWhileRunning.disabled).toBeTrue();

    fixture.componentInstance.setMenuOpen('p1', false);
    const stopWhileStopped = (await openMenu('p2'))[1];
    expect(stopWhileStopped.disabled).toBeTrue();
  });

  it('keeps every action listed but disabled for a self-registering row, with a reason', async () => {
    configure([plugin('p1', { managed: false, state: 'running' })]);
    fixture = await create();

    const items = await openMenu('p1');

    expect(items.length).toBe(3);
    expect(items.every((item) => item.disabled)).toBeTrue();
    expect((fixture.nativeElement.querySelector('.managed-plugins-menu-note') as HTMLElement).textContent)
      .toContain('starts and stops itself');
  });

  it('shows no note for a supervised row, whose actions are disabled only by state', async () => {
    configure([plugin('p1', { managed: true })]);
    fixture = await create();

    await openMenu('p1');

    expect(fixture.nativeElement.querySelector('.managed-plugins-menu-note')).toBeFalsy();
  });

  it('closes the menu once an action is triggered', async () => {
    configure([plugin('p1', { state: 'stopped' })]);
    fixture = await create();

    const [start] = await openMenu('p1');
    start.click();
    await fixture.whenStable();

    expect(fixture.componentInstance.openMenuPluginId()).toBeNull();
  });

  it('omits the version chip when no version is known', async () => {
    configure([plugin('p1', { version: '' })]);
    fixture = await create();

    const row = fixture.nativeElement.querySelector('.managed-plugins-row');
    expect(row.querySelector('.managed-plugins-version')).toBeFalsy();
  });

  it('shows the bootstrap output only when non-empty', async () => {
    configure([
      plugin('p1', { bootstrapOutput: [] }),
      plugin('p2', { bootstrapOutput: ['starting up', 'listening on :0'] }),
    ]);
    fixture = await create();

    const rows = fixture.nativeElement.querySelectorAll('.managed-plugins-row');
    expect(rows[0].querySelector('.managed-plugins-output')).toBeFalsy();
    expect(rows[1].querySelector('.managed-plugins-output')).toBeTruthy();
  });

  it('expands the bootstrap output on toggle', async () => {
    configure([plugin('p1', { bootstrapOutput: ['line one'] })]);
    fixture = await create();

    expect(fixture.nativeElement.querySelector('.managed-plugins-output-body')).toBeFalsy();

    fixture.nativeElement.querySelector('.managed-plugins-output-toggle').click();
    fixture.detectChanges();

    const body = fixture.nativeElement.querySelector('.managed-plugins-output-body');
    expect(body.textContent).toContain('line one');
  });

  describe('actions call the service', () => {
    it('start', async () => {
      configure([plugin('p1', { state: 'stopped' })]);
      fixture = await create();

      await fixture.componentInstance.start(fixture.componentInstance.plugins()[0]);

      expect(runtimeSpy.start).toHaveBeenCalledWith('p1');
    });

    it('stop', async () => {
      configure([plugin('p1', { state: 'running' })]);
      fixture = await create();

      await fixture.componentInstance.stop(fixture.componentInstance.plugins()[0]);

      expect(runtimeSpy.stop).toHaveBeenCalledWith('p1');
    });

    it('restart', async () => {
      configure([plugin('p1', { state: 'running' })]);
      fixture = await create();

      await fixture.componentInstance.restart(fixture.componentInstance.plugins()[0]);

      expect(runtimeSpy.restart).toHaveBeenCalledWith('p1');
    });

    it('toasts the error message when an action fails', async () => {
      configure([plugin('p1', { state: 'running' })]);
      fixture = await create();
      const toastSpy = spyOn(TestBed.inject(ToastService), 'show');
      runtimeSpy.stop.and.resolveTo({ success: false, error: { code: 'nope', message: 'Could not stop it.' } });

      await fixture.componentInstance.stop(fixture.componentInstance.plugins()[0]);

      expect(toastSpy).toHaveBeenCalledWith('Could not stop it.', { variant: 'error' });
    });
  });
});
