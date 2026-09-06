import { WritableSignal, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PairedPlugin, PluginRuntimeInfo } from '@macro-deck/runtime';
import { ToastService } from '@shared';
import { DeveloperModeService } from '../../../../../../services/developer-mode.service';
import { PluginPairingService } from '../../../../../../services/plugin-pairing.service';
import { PluginRuntimeService } from '../../../../../../services/plugin-runtime.service';
import { PairedPluginsSectionComponent } from './paired-plugins-section.component';
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

describe('PairedPluginsSectionComponent', () => {
  let fixture: ComponentFixture<PairedPluginsSectionComponent>;
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
      imports: [PairedPluginsSectionComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: PluginRuntimeService, useValue: runtimeSpy },
        { provide: PluginPairingService, useValue: pairingSpy },
        { provide: DeveloperModeService, useValue: developerModeSpy },
      ],
    });
  }

  async function create(): Promise<ComponentFixture<PairedPluginsSectionComponent>> {
    const f = TestBed.createComponent(PairedPluginsSectionComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

    it('renders a paired registration', async () => {
      configure([], [pairedPlugin('p1', { displayName: 'Example Tools', online: true })]);
      fixture = await create();

      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('Example Tools');
      expect(text).toContain('p1');
      expect(text).toContain('Online');
    });

    it('revokes the paired plugin through the service on confirm', async () => {
      configure([], [pairedPlugin('p1')]);
      fixture = await create();

      fixture.componentInstance.requestRevokePaired(fixture.componentInstance.pairedPlugins()[0]);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('shared-confirmation-modal')).toBeTruthy();

      await fixture.componentInstance.confirmRevokePaired();

      expect(pairingSpy.revoke).toHaveBeenCalledWith('p1');
    });

    it('toasts an error when revoking a paired plugin fails', async () => {
      configure([], [pairedPlugin('p1')]);
      fixture = await create();
      const toastSpy = spyOn(TestBed.inject(ToastService), 'show');
      pairingSpy.revoke.and.rejectWith(new Error('Could not reach the host.'));

      fixture.componentInstance.requestRevokePaired(fixture.componentInstance.pairedPlugins()[0]);
      await fixture.componentInstance.confirmRevokePaired();

      expect(toastSpy).toHaveBeenCalledWith('Could not reach the host.', { variant: 'error' });
    });

    it('stays hidden with no error state when the pairing endpoints are unavailable on this transport', async () => {
      configure([], [], { pairingUnavailable: true });
      fixture = await create();

      expect(fixture.nativeElement.textContent).not.toContain('Paired plugins');
      expect(fixture.nativeElement.querySelector('.plugin-tokens-error')).toBeFalsy();
    });
});
