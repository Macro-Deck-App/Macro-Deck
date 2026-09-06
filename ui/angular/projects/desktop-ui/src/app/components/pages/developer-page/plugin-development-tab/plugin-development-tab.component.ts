import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@shared';
import { RailItemComponent } from '../../../rail-page/rail-item.component';
import { RailPageComponent } from '../../../rail-page/rail-page.component';
import { DeveloperModeService } from '../../../../services/developer-mode.service';
import { PluginCompatibilityService } from '../../../../services/plugin-compatibility.service';
import { PluginPairingService } from '../../../../services/plugin-pairing.service';
import { PluginRuntimeService } from '../../../../services/plugin-runtime.service';
import { PluginTokenService } from '../../../../services/plugin-token.service';
import { CompatibilitySectionComponent } from './sections/compatibility-section/compatibility-section.component';
import { CredentialsSectionComponent } from './sections/credentials-section/credentials-section.component';
import { ManagedPluginsSectionComponent } from './sections/managed-plugins-section/managed-plugins-section.component';
import { PairedPluginsSectionComponent } from './sections/paired-plugins-section/paired-plugins-section.component';
import { SessionsSectionComponent } from './sections/sessions-section/sessions-section.component';
import { DeveloperModeGateComponent } from './developer-mode-gate.component';

export const PLUGIN_DEVELOPMENT_TAB = 'plugin-development';

export type PluginDevelopmentSection =
  | 'managed-plugins'
  | 'paired-plugins'
  | 'credentials'
  | 'sessions'
  | 'compatibility';

const SECTIONS: readonly PluginDevelopmentSection[] =
  ['managed-plugins', 'paired-plugins', 'credentials', 'sessions', 'compatibility'];
const DEFAULT_SECTION: PluginDevelopmentSection = 'managed-plugins';

const LEGACY_TAB_SECTIONS: Readonly<Record<string, PluginDevelopmentSection>> = {
  'plugin-tokens': 'credentials',
  'managed-plugins': 'managed-plugins',
  compatibility: 'compatibility',
};

function isSection(value: string | null): value is PluginDevelopmentSection {
  return value !== null && (SECTIONS as readonly string[]).includes(value);
}

@Component({
  selector: 'app-plugin-development-tab',
  standalone: true,
  imports: [
    RailItemComponent,
    RailPageComponent,
    TranslatePipe,
    CompatibilitySectionComponent,
    DeveloperModeGateComponent,
    CredentialsSectionComponent,
    ManagedPluginsSectionComponent,
    PairedPluginsSectionComponent,
    SessionsSectionComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plugin-development-tab.component.html',
  styleUrls: ['./plugin-development-tab.component.scss'],
})
export class PluginDevelopmentTabComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly developerMode = inject(DeveloperModeService);
  private readonly tokenService = inject(PluginTokenService);
  private readonly pairingService = inject(PluginPairingService);
  private readonly runtimeService = inject(PluginRuntimeService);
  private readonly compatibilityService = inject(PluginCompatibilityService);

  readonly activeSection = signal<PluginDevelopmentSection>(DEFAULT_SECTION);

  readonly managedPluginsCount = computed(() => this.runtimeService.plugins().length);

  readonly pairedPluginsCount = computed(() => this.pairingService.pairedPlugins().length);

  readonly credentialsCount = computed(() => this.tokenService.tokens().length);

  readonly sessionsCount = computed(() =>
    this.tokenService.sessions().filter(session => session.origin === 'self-registered').length);

  readonly compatibilityCount = computed(() => this.compatibilityService.reports().length);

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe(params => {
      const section = params.get('section');
      if (isSection(section)) {
        this.activeSection.set(section);
        return;
      }
      this.activeSection.set(LEGACY_TAB_SECTIONS[params.get('tab') ?? ''] ?? DEFAULT_SECTION);
    });

    void this.developerMode.ensureLoaded();

    effect(() => {
      if (!this.developerMode.enabled()) {
        return;
      }
      void this.runtimeService.load();
      void this.tokenService.load();
      void this.pairingService.load();
      void this.compatibilityService.load();
    });
  }

  isSectionActive(section: PluginDevelopmentSection): boolean {
    return this.activeSection() === section;
  }

  selectSection(section: PluginDevelopmentSection): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        tab: PLUGIN_DEVELOPMENT_TAB,
        section: section === DEFAULT_SECTION ? null : section,
      },
      queryParamsHandling: 'merge',
    });
  }
}
