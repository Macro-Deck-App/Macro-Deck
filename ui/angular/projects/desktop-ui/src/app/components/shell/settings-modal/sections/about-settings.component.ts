import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { GetAboutInfoResponse } from '@macro-deck/runtime';
import { ApiService, LocalizationService, SettingsRowComponent, SettingsSectionComponent, TranslatePipe } from '@shared';
import { ExternalLinkService } from '../../../../services/external-link.service';
import { UpdateCheckComponent } from './update-check.component';
import { formatBuildVersion } from '../../../../services';

const LICENSE_URL = 'https://www.apache.org/licenses/LICENSE-2.0';

@Component({
  selector: 'app-about-settings',
  standalone: true,
  imports: [SettingsSectionComponent, SettingsRowComponent, UpdateCheckComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './about-settings.component.html',
  styleUrls: ['./about-settings.component.scss'],
})
export class AboutSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly externalLinks = inject(ExternalLinkService);
  private readonly localization = inject(LocalizationService);

  readonly info = signal<GetAboutInfoResponse | null>(null);
  readonly loadFailed = signal(false);

  readonly shellVersion = signal<string | null>(null);

  readonly buildTime = computed(() => {
    const timestamp = this.info()?.buildTimestamp;
    if (!timestamp) {
      return null;
    }
    const parsed = new Date(timestamp);
    if (Number.isNaN(parsed.getTime())) return timestamp;
    const { locale, hourCycle } = this.localization.timeLocale();
    return parsed.toLocaleString(locale, { hourCycle });
  });

  readonly licenseLabel = computed(() => {
    const license = this.info()?.license;
    return license && license !== 'Apache-2.0' ? license : null;
  });

  readonly versionLabel = computed(() => {
    const info = this.info();
    return info ? formatBuildVersion(info.version, info.isDevelopmentBuild, info.commit) : '-';
  });

  constructor() {
    void this.load();
    void this.loadShellInfo();
  }

  openLicense(): void {
    this.externalLinks.open(LICENSE_URL);
  }

  private async load(): Promise<void> {
    try {
      this.info.set(await this.api.getAboutInfo());
    } catch {
      this.loadFailed.set(true);
    }
  }

  private async loadShellInfo(): Promise<void> {
    if (!window.macroDeckShell?.getShellInfo) {
      return;
    }
    try {
      const info = await window.macroDeckShell.getShellInfo();
      const webview = info.webviewVersion ? ` (WebView ${info.webviewVersion})` : '';
      this.shellVersion.set(`${info.shellVersion} - Tauri ${info.tauriVersion}${webview}`);
    } catch {
      this.shellVersion.set(null);
    }
  }
}
