import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AppStrings, PluginRuntimeInfo } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { PluginRuntimeService } from '../../../services/plugin-runtime.service';
import { StoreTrustBadgeComponent } from './store-trust-badge.component';
import { provideLocalizationTesting } from '../../../../testing/localization-test-support';

describe('StoreTrustBadgeComponent', () => {
  let fixture: ComponentFixture<StoreTrustBadgeComponent>;
  let runtimePlugins: ReturnType<typeof signal<PluginRuntimeInfo[]>>;

  function setup(pluginId: string | null, takenOver: boolean): void {
    runtimePlugins = signal([
      { pluginId: 'app.example.plugin', takenOverByDevelopmentBuild: takenOver } as PluginRuntimeInfo,
    ]);

    TestBed.configureTestingModule({
      imports: [StoreTrustBadgeComponent],
      providers: [
        provideZonelessChangeDetection(),
        ...provideLocalizationTesting(),
        { provide: PluginRuntimeService, useValue: { plugins: runtimePlugins } },
      ],
    });

    fixture = TestBed.createComponent(StoreTrustBadgeComponent);
    fixture.componentRef.setInput('kind', 'Plugin');
    fixture.componentRef.setInput('trust', 'PublisherVerified');
    fixture.componentRef.setInput('pluginId', pluginId);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function translate(key: string): string {
    return TestBed.inject(LocalizationService).translateKey(key);
  }

  it('shows the verified badge while no development build has taken the plugin over', () => {
    setup('app.example.plugin', false);

    expect(text()).toContain(translate(AppStrings.Store.PublisherVerified));
  });

  it('shows the development build marker instead of verified during a takeover', () => {
    setup('app.example.plugin', true);

    expect(text()).toContain(translate('macrodeck.app:Developer.ManagedPlugins.TakenOverBadge'));
    expect(text()).not.toContain(translate(AppStrings.Store.PublisherVerified));
  });

  it('keeps the verified badge when no plugin id is given', () => {
    setup(null, true);

    expect(text()).toContain(translate(AppStrings.Store.PublisherVerified));
  });
});
