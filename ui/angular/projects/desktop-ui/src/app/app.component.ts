import { Component, computed, effect, inject, OnInit, OnDestroy, ChangeDetectionStrategy, signal, untracked } from '@angular/core';

import { RouterOutlet } from '@angular/router';
import { AppStrings, GetBackupStatusResponse } from '@macro-deck/runtime';
import { ApiService, AuthService, KeyRingService, LoginFormComponent, LocalizationService, NotificationDialogHostComponent, ThemeService, ToastHostComponent, VariableService, UiModalHostComponent } from '@shared';
import { PluginPairingDialogComponent } from './components/overlay/plugin-pairing-dialog/plugin-pairing-dialog.component';
import { PluginAdbConsentDialogComponent } from './components/overlay/plugin-adb-consent-dialog/plugin-adb-consent-dialog.component';
import { PluginCompanionConsentDialogComponent } from './components/overlay/plugin-companion-consent-dialog/plugin-companion-consent-dialog.component';
import { HostSessionService } from './services/host-session.service';
import { SplashComponent, SplashStep } from './components/splash';
import { FirstRunWizardComponent } from './components/first-run-wizard/first-run-wizard.component';
import { KeyRingUnlockGateComponent } from './components/key-ring-unlock-gate/key-ring-unlock-gate.component';
import { MigrationOfferModalComponent } from './components/migration/migration-offer-modal/migration-offer-modal.component';
import { OnboardingWizardComponent } from './components/onboarding/onboarding-wizard.component';
import { MigrationWizardComponent } from './components/migration/migration-wizard/migration-wizard.component';
import { ProfileService } from '@shared';
import { DeepLinkService, FileOpenService, MenuActionService, NavigationService, FolderService, UpdateService } from './services';
import { MigrationWizardService } from './services/migration-wizard.service';
import { BackupService } from './services/backup.service';
import { backupEta, backupPercent, isActiveBackupStage, isTerminalBackupStage } from './util/backup-progress-display';

const BOOTSTRAP_RETRY_MS = 1500;

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    RouterOutlet, SplashComponent, FirstRunWizardComponent,
    KeyRingUnlockGateComponent, LoginFormComponent, ToastHostComponent, PluginPairingDialogComponent, PluginAdbConsentDialogComponent,
    PluginCompanionConsentDialogComponent,
    UiModalHostComponent, NotificationDialogHostComponent, OnboardingWizardComponent, MigrationOfferModalComponent, MigrationWizardComponent,
  ],
  template: `
    @if (keyRing.locked()) {
      <!-- Ahead of the auth switch below: a locked host 503s everything auth.bootstrap() and
           api.connect() need, so this must pre-empt them rather than fall through to the splash
           screen's reconnect loop. See probeKeyRingWithRetry(). -->
      <app-key-ring-unlock-gate></app-key-ring-unlock-gate>
    } @else {
      @switch (auth.state()) {
        @case ('setupRequired') {
          <app-first-run-wizard></app-first-run-wizard>
        }
        @case ('loggedOut') {
          <!-- Non-loopback transport (e.g. a future remote full UI): admin login. -->
          <div class="auth-gate">
            <shared-login-form scope="admin"></shared-login-form>
          </div>
        }
        @case ('authenticated') {
          @if (installingUpdate() || !hasConnected()) {
            <app-splash [status]="splashStatus()" [progress]="splashProgress()" [details]="splashDetails()" [steps]="splashSteps()" [cancelLabel]="splashCancelLabel()" (cancel)="cancelUpdateBackup()"></app-splash>
          } @else {
            <div class="shell-outlet" [inert]="!isConnected()">
              <router-outlet></router-outlet>
            </div>
            @if (!isConnected()) {
              <app-splash [overlay]="true" [status]="splashStatus()"></app-splash>
            }
          }
        }
        @default {
          <app-splash [status]="splashStatus()"></app-splash>
        }
      }
    }
    <shared-plugin-pairing-dialog></shared-plugin-pairing-dialog>
    <shared-plugin-adb-consent-dialog></shared-plugin-adb-consent-dialog>
    <shared-plugin-companion-consent-dialog></shared-plugin-companion-consent-dialog>
    <!-- Mounted at the shell, not on the deck page: an action can be run from the editor or a script
         page too, and its modal has to appear wherever the user set it going. -->
    <shared-ui-modal-host></shared-ui-modal-host>
    <shared-notification-dialog-host></shared-notification-dialog-host>
    <!-- Both self-gating, like shared-plugin-pairing-dialog above - see their class docs for when
         they present themselves. The migration offer waits for the onboarding wizard to be done. -->
    <app-onboarding-wizard></app-onboarding-wizard>
    <app-migration-offer-modal></app-migration-offer-modal>
    @if (migrationWizard.isOpen()) {
      <app-migration-wizard></app-migration-wizard>
    }
    <shared-toast-host></shared-toast-host>
    `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: [`
    .shell-outlet {
      display: contents;
    }

    .auth-gate {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 100vw;
      height: 100vh;
      background: var(--color-bg-primary);
    }
  `]
})
export class AppComponent implements OnInit, OnDestroy {
  protected readonly api = inject(ApiService);
  protected readonly auth = inject(AuthService);
  protected readonly keyRing = inject(KeyRingService);
  private readonly navigationService = inject(NavigationService);
  private readonly fileOpen = inject(FileOpenService);
  private readonly deepLinks = inject(DeepLinkService);
  private readonly menuActions = inject(MenuActionService);
  private readonly hostSession = inject(HostSessionService);
  private readonly profileService = inject(ProfileService);
  private readonly folderService = inject(FolderService);
  private readonly themeService = inject(ThemeService);
  private readonly localizationService = inject(LocalizationService);
  private readonly variableService = inject(VariableService);
  protected readonly migrationWizard = inject(MigrationWizardService);
  private readonly updateService = inject(UpdateService);
  private readonly backupService = inject(BackupService);
  private readonly updateBackupId = signal<string | null>(null);
  private staleBackupId: string | null | undefined;
  private bootstrapRetryTimer: ReturnType<typeof setTimeout> | null = null;
  private keyRingProbeTimer: ReturnType<typeof setTimeout> | null = null;

  readonly isConnected = computed(() => this.api.connectionStateSignal() === 'connected');

  protected readonly hasConnected = signal(false);

  private readonly blockKeysWhileReconnecting = (event: KeyboardEvent): void => {
    if (this.hasConnected() && !this.isConnected()) {
      event.stopImmediatePropagation();
    }
  };

  protected readonly installingUpdate = computed(() => this.updateService.phase() === 'installing');

  private readonly updateBackup = computed(() => {
    const id = this.updateBackupId();
    const operation = this.backupService.operation();
    return id !== null && operation?.operationId === id ? operation : null;
  });

  private readonly updateBackupRunning = computed(() => {
    const operation = this.updateBackup();
    return !!operation && isActiveBackupStage(operation.stage);
  });

  private readonly updateBackupDone = computed(() => this.updateBackup()?.stage === 'Completed');

  protected readonly splashProgress = computed(() =>
    this.installingUpdate() && this.updateBackupRunning() ? backupPercent(this.updateBackup()) : null);

  protected readonly splashDetails = computed<string[]>(() => {
    if (this.hostSession.stopping() || !this.installingUpdate() || !this.updateBackupRunning()) {
      return [];
    }
    const eta = backupEta(this.updateBackup(), this.localizationService);
    return eta ? [eta] : [];
  });

  protected readonly splashSteps = computed<SplashStep[]>(() => {
    if (this.hostSession.stopping() || !this.installingUpdate()) {
      return [];
    }
    if (!this.updateBackupRunning() && !this.updateBackupDone()) {
      return [];
    }
    const backingUp = this.updateBackupRunning();
    return [
      { label: this.localizationService.translateKey(AppStrings.Shell.Splash.StepBackup), state: backingUp ? 'current' : 'done' },
      { label: this.localizationService.translateKey(AppStrings.Shell.Splash.StepInstall), state: backingUp ? 'pending' : 'current' },
    ];
  });

  protected readonly splashCancelLabel = computed(() =>
    !this.hostSession.stopping() && this.installingUpdate() && this.updateBackupRunning()
      ? this.localizationService.translateKey(AppStrings.Shell.Splash.CancelUpdate)
      : null);

  protected readonly splashStatus = computed(() => {
    if (this.hostSession.stopping()) {
      return this.localizationService.translateKey(AppStrings.Shell.Splash.Stopping);
    }
    if (this.installingUpdate()) {
      if (this.updateBackupRunning()) {
        const percent = backupPercent(this.updateBackup());
        return percent === null
          ? this.localizationService.translateKey(AppStrings.Shell.Splash.CreatingBackup)
          : this.localizationService.translateKey(AppStrings.Shell.Splash.CreatingBackupProgress, { percent });
      }
      return this.localizationService.translateKey(this.updateBackupDone()
        ? AppStrings.Shell.Splash.InstallingUpdate
        : AppStrings.Shell.Splash.PreparingUpdate);
    }
    if (!this.isConnected() && this.updateService.installFailed()) {
      return this.updateService.error()
        ?? this.localizationService.translateKey(AppStrings.Settings.Update.InstallFailed);
    }
    return this.localizationService.translateKey(AppStrings.WebClient.Connecting);
  });

  constructor() {
    effect(() => {
      const installing = this.installingUpdate();
      const operation = this.backupService.operation();
      untracked(() => this.trackUpdateBackup(installing, operation));
    });

    effect(() => {
      if (this.auth.state() === 'authenticated') {
        this.api.connect();
      }
    });

    effect(() => {
      if (this.auth.state() !== 'authenticated') {
        this.hasConnected.set(false);
      }
      if (this.isConnected() && this.auth.state() === 'authenticated') {
        this.hasConnected.set(true);
        void this.initializeApp();
        // Only now: a file the OS opened during startup waits in the shell's queue until something
        // can actually import it, so this must never run against a host that cannot answer yet.
        this.fileOpen.start();
        this.deepLinks.start();
      }
    });
  }

  ngOnInit(): void {
    // Before the host is even reachable, unlike the opened-file queue: the only menu action so far
    // opens a modal the UI owns on its own, and picking it is what created this page in the first
    // place - so it must not wait for a connection.
    this.menuActions.start();
    // Capture phase on window runs before every document-level shortcut, so none of them acts on
    // the page hidden under the reconnecting overlay.
    window.addEventListener('keydown', this.blockKeysWhileReconnecting, true);
    this.hostSession.start();
    void this.probeKeyRingWithRetry();
  }

  private async probeKeyRingWithRetry(): Promise<void> {
    await this.keyRing.probe();
    if (this.keyRing.status() === null) {
      this.keyRingProbeTimer = setTimeout(() => {
        this.keyRingProbeTimer = null;
        void this.probeKeyRingWithRetry();
      }, BOOTSTRAP_RETRY_MS);
      return;
    }
    if (!this.keyRing.locked()) {
      void this.bootstrapWithRetry();
    }
  }

  private async bootstrapWithRetry(): Promise<void> {
    await this.auth.bootstrap();
    if (this.auth.state() === 'unknown') {
      this.bootstrapRetryTimer = setTimeout(() => {
        this.bootstrapRetryTimer = null;
        void this.bootstrapWithRetry();
      }, BOOTSTRAP_RETRY_MS);
    }
  }

  private async initializeApp(): Promise<void> {
    try {
      // The footer and Settings -> About intentionally use the same host build information.
      const aboutInfo = await this.api.getAboutInfo();
      console.log('Application version:', aboutInfo.version);
      this.navigationService.setAppVersion(
        aboutInfo.version,
        aboutInfo.isBeta,
        aboutInfo.isDevelopmentBuild,
        aboutInfo.commit
      );

      await this.themeService.loadFromHost();
      await this.localizationService.loadFromHost();

      // Load profiles; the active profile drives folder loading (see FolderService). On this loopback
      // window auth.startupProfileId() is always null: bootstrap() returns early on status.trusted
      // without ever calling applySession, and the loopback desktop UI is not a device itself in the
      // first place (ADR 0003). The argument only matters for a remote admin-UI login, which
      // does mint a device and can carry a startup-profile assignment (issue #251).
      await this.profileService.loadProfiles(this.auth.startupProfileId());
      console.log('Profiles loaded:', this.profileService.profiles().length);

      await this.variableService.loadVariables();
    } catch (error) {
      console.error('Failed to initialize app:', error);
    }
  }

  // A finished backup left over from an earlier attempt is not this install's; a running one already is,
  // since its first event can arrive before the installing phase does.
  private trackUpdateBackup(installing: boolean, operation: GetBackupStatusResponse | null): void {
    if (!installing) {
      this.staleBackupId = undefined;
      this.updateBackupId.set(null);
      return;
    }
    if (this.staleBackupId === undefined) {
      this.staleBackupId = operation && isTerminalBackupStage(operation.stage) ? operation.operationId ?? null : null;
    }
    if (this.updateBackupId() === null
      && operation?.kind === 'Create'
      && operation.trigger === 'BeforeHostUpdate'
      && operation.operationId
      && operation.operationId !== this.staleBackupId) {
      this.updateBackupId.set(operation.operationId);
    }
  }

  protected cancelUpdateBackup(): void {
    const operationId = this.updateBackup()?.operationId;
    if (operationId) {
      void this.backupService.cancelOperation(operationId);
    }
  }

  ngOnDestroy(): void {
    window.removeEventListener('keydown', this.blockKeysWhileReconnecting, true);
    if (this.bootstrapRetryTimer !== null) {
      clearTimeout(this.bootstrapRetryTimer);
    }
    if (this.keyRingProbeTimer !== null) {
      clearTimeout(this.keyRingProbeTimer);
    }
  }
}
