import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { AppStrings, ConnectionEndpoint, GetConnectionInfoResponse } from '@macro-deck/runtime';
import { ApiService, AuthService, ButtonComponent, LocalizationService, ModalComponent, ToastService, TranslatePipe } from '@shared';
import { BackupService } from '../../services/backup.service';
import { ExternalLinkService } from '../../services/external-link.service';
import { OnboardingService } from '../../services/onboarding.service';
import { ConfirmationModalComponent } from '../overlay/confirmation-modal/confirmation-modal.component';
import { LoadingStateComponent } from '../feedback/loading-state/loading-state.component';
import { RecoveryKeyModalComponent } from '../backup/recovery-key-modal/recovery-key-modal.component';

type OnboardingStep = 'welcome' | 'connect' | 'recovery-key' | 'links';

const STEPS: OnboardingStep[] = ['welcome', 'connect', 'recovery-key', 'links'];

const LINKS = [
  {
    url: 'https://github.com/Macro-Deck-App/Macro-Deck',
    title: AppStrings.Onboarding.Links.GitHub,
    description: AppStrings.Onboarding.Links.GitHubDescription,
  },
  {
    url: 'https://github.com/sponsors/Macro-Deck-App',
    title: AppStrings.Onboarding.Links.Sponsors,
    description: AppStrings.Onboarding.Links.SponsorsDescription,
  },
  {
    url: 'https://ko-fi.com/manuelmayer',
    title: AppStrings.Onboarding.Links.KoFi,
    description: AppStrings.Onboarding.Links.KoFiDescription,
  },
  {
    url: 'https://discord.macro-deck.app',
    title: AppStrings.Onboarding.Links.Discord,
    description: AppStrings.Onboarding.Links.DiscordDescription,
  },
] as const;

type RecoveryKeyModalState =
  | { kind: 'none' }
  | { kind: 'revealConfirm' }
  | { kind: 'keyModal'; mode: 'created' | 'revealed'; key: string };

// Mounted unconditionally in the app shell: it renders nothing until OnboardingService.visible says
// otherwise, and a failed load leaves the latch open so the next reconnect tries again.
@Component({
  selector: 'app-onboarding-wizard',
  standalone: true,
  imports: [
    ModalComponent,
    ButtonComponent,
    TranslatePipe,
    LoadingStateComponent,
    ConfirmationModalComponent,
    RecoveryKeyModalComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './onboarding-wizard.component.html',
  styleUrls: ['./onboarding-wizard.component.scss'],
})
export class OnboardingWizardComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly externalLinks = inject(ExternalLinkService);
  private readonly backupService = inject(BackupService);
  private readonly toastService = inject(ToastService);
  protected readonly localization = inject(LocalizationService);
  protected readonly onboarding = inject(OnboardingService);
  protected readonly appStrings = AppStrings;
  protected readonly links = LINKS;

  protected readonly step = signal<OnboardingStep>('welcome');
  protected readonly connection = signal<GetConnectionInfoResponse | null>(null);
  protected readonly connectionLoading = signal(false);

  protected readonly recoveryKeyState = this.backupService.recoveryKey;
  protected readonly recoveryKeyLoadState = signal<'loading' | 'loaded' | 'failed'>('loading');
  protected readonly recoveryKeyWorking = signal(false);
  protected readonly recoveryKeyModal = signal<RecoveryKeyModalState>({ kind: 'none' });
  private readonly recoveryKeySatisfiedAtEntry = signal(false);
  private readonly recoveryKeyAcknowledged = signal(false);
  protected readonly recoveryKeyActionFailed = signal<'none' | 'reveal' | 'create'>('none');
  private recoveryKeyStepEntered = false;

  // Not keyed off exportedAt: the host stamps that before the key reaches the screen, so it would
  // unlock the moment the key modal is dismissed without the user having stored anything.
  protected readonly recoveryKeySatisfied = computed(
    () => this.recoveryKeySatisfiedAtEntry() || this.recoveryKeyAcknowledged());

  private readonly canLeaveRecoveryKeyStep = computed(() => {
    if (this.recoveryKeySatisfied() || this.recoveryKeyActionFailed() !== 'none') {
      return true;
    }
    if (this.recoveryKeyLoadState() === 'failed') {
      return true;
    }
    return this.recoveryKeyState()?.availability === 'Missing';
  });

  private readonly canLeaveCurrentStep = computed(
    () => this.step() !== 'recovery-key' || this.canLeaveRecoveryKeyStep());

  protected readonly blockedOnCurrentStep = computed(() => !this.canLeaveCurrentStep());

  private readonly isConnected = computed(() => this.api.connectionStateSignal() === 'connected');
  private presented = false;
  private checking = false;

  protected readonly stepNumber = computed(() => STEPS.indexOf(this.step()) + 1);
  protected readonly stepCount = STEPS.length;
  protected readonly isFirstStep = computed(() => this.step() === STEPS[0]);
  protected readonly isLastStep = computed(() => this.step() === STEPS[STEPS.length - 1]);

  // The host lists an http and an https entry per address, so group by scheme. Order within a group
  // is the host's: only the user knows which of a VPN's or several NICs' addresses their phone reaches.
  protected readonly endpoints = computed<ConnectionEndpoint[]>(
    () => [...this.connection()?.endpoints ?? []].sort((a, b) => Number(a.ssl) - Number(b.ssl)));

  protected readonly stepHeading = computed(() => {
    switch (this.step()) {
      case 'welcome': return this.localization.translateKey(AppStrings.Onboarding.Welcome.Title);
      case 'connect': return this.localization.translateKey(AppStrings.Onboarding.Connect.Title);
      case 'recovery-key': return this.localization.translateKey(AppStrings.Onboarding.RecoveryKey.Title);
      case 'links': return this.localization.translateKey(AppStrings.Onboarding.Links.Title);
    }
  });

  protected readonly stepProgress = computed(() => this.localization.translateKey(
    AppStrings.Onboarding.StepProgress, { current: this.stepNumber(), total: this.stepCount }));

  constructor() {
    effect(() => {
      if (this.presented) {
        return;
      }
      if (this.isConnected() && this.auth.state() === 'authenticated' && !this.checking) {
        this.checking = true;
        void this.checkAndPresent();
      }
    });
  }

  protected next(): void {
    if (!this.canLeaveCurrentStep()) {
      return;
    }

    const index = STEPS.indexOf(this.step());
    if (index >= STEPS.length - 1) {
      return;
    }

    this.step.set(STEPS[index + 1]);
    this.onStepEntered(this.step());
  }

  protected back(): void {
    const index = STEPS.indexOf(this.step());
    if (index <= 0) {
      return;
    }

    this.step.set(STEPS[index - 1]);
    this.onStepEntered(this.step());
  }

  protected finish(): void {
    if (!this.canLeaveCurrentStep()) {
      return;
    }

    void this.onboarding.complete();
  }

  protected url(endpoint: ConnectionEndpoint, target: 'config' | 'client'): string {
    const base = `${endpoint.ssl ? 'https' : 'http'}://${endpoint.address}:${endpoint.port}`;

    return target === 'client' ? base : `${base}/admin`;
  }

  protected openTarget(endpoint: ConnectionEndpoint, target: 'config' | 'client'): void {
    this.externalLinks.open(this.url(endpoint, target));
  }

  protected openLink(url: string): void {
    this.externalLinks.open(url);
  }

  protected retryLoadRecoveryKey(): void {
    void this.loadRecoveryKey();
  }

  protected async createRecoveryKey(): Promise<void> {
    this.recoveryKeyWorking.set(true);
    this.recoveryKeyActionFailed.set('none');
    try {
      const outcome = await this.backupService.createRecoveryKey();
      if (outcome.status === 'success') {
        this.recoveryKeyModal.set({ kind: 'keyModal', mode: 'created', key: outcome.key });
      } else {
        // No skip and no close button, so a step the host will not let the user finish has to let
        // them past rather than hold them there for good.
        this.recoveryKeyActionFailed.set('create');
        this.toastService.show(outcome.message, { variant: 'error' });
        // A key that raced in answers success with key: null, which lands here as an error. Reload so
        // the step re-renders into the show branch instead of a stale None branch.
        await this.backupService.loadRecoveryKeyState();
        if (this.recoveryKeyState()?.availability !== 'None') {
          this.recoveryKeyActionFailed.set('none');
        }
      }
    } finally {
      this.recoveryKeyWorking.set(false);
    }
  }

  protected requestReveal(): void {
    this.recoveryKeyActionFailed.set('none');
    this.recoveryKeyModal.set({ kind: 'revealConfirm' });
  }

  protected cancelReveal(): void {
    this.recoveryKeyModal.set({ kind: 'none' });
  }

  protected async confirmReveal(): Promise<void> {
    this.recoveryKeyModal.set({ kind: 'none' });
    this.recoveryKeyWorking.set(true);
    try {
      const outcome = await this.backupService.revealRecoveryKey();
      if (outcome.status === 'success') {
        this.recoveryKeyModal.set({ kind: 'keyModal', mode: 'revealed', key: outcome.key });
      } else {
        // Reveal is loopback-only, so a remote /admin session can never get the key and must not be
        // held here. A transient failure lands here too, which is why the reveal button stays.
        this.recoveryKeyActionFailed.set('reveal');
      }
    } finally {
      this.recoveryKeyWorking.set(false);
    }
  }

  protected async onRecoveryKeyAcknowledged(): Promise<void> {
    this.recoveryKeyModal.set({ kind: 'none' });
    const result = await this.backupService.acknowledgeRecoveryKey();
    if (result.ok) {
      this.recoveryKeyAcknowledged.set(true);
    } else {
      this.toastService.show(result.error, { variant: 'error' });
    }
  }

  protected closeRecoveryKeyModal(): void {
    this.recoveryKeyModal.set({ kind: 'none' });
  }

  private async checkAndPresent(): Promise<void> {
    try {
      await this.onboarding.load();
    } finally {
      this.checking = false;
    }

    if (this.onboarding.state() === 'unknown') {
      return;
    }

    this.presented = true;
    this.onboarding.present();
  }

  private onStepEntered(step: OnboardingStep): void {
    if (step === 'connect') {
      void this.loadConnection();
    } else if (step === 'recovery-key') {
      void this.enterRecoveryKeyStep();
    }
  }

  private async loadConnection(): Promise<void> {
    if (this.connection() || this.connectionLoading()) {
      return;
    }

    this.connectionLoading.set(true);
    try {
      this.connection.set(await this.api.getConnectionInfo());
    } catch {
      this.connection.set(null);
    } finally {
      this.connectionLoading.set(false);
    }
  }

  private async enterRecoveryKeyStep(): Promise<void> {
    if (this.recoveryKeyStepEntered) {
      return;
    }
    this.recoveryKeyStepEntered = true;

    if (this.recoveryKeyState()) {
      this.recoveryKeyLoadState.set('loaded');
      this.captureInitialSatisfaction();
      return;
    }

    await this.loadRecoveryKey();
  }

  private async loadRecoveryKey(): Promise<void> {
    this.recoveryKeyLoadState.set('loading');
    const ok = await this.backupService.loadRecoveryKeyState();
    this.recoveryKeyLoadState.set(ok ? 'loaded' : 'failed');
    if (ok) {
      this.captureInitialSatisfaction();
    }
  }

  private captureInitialSatisfaction(): void {
    const state = this.recoveryKeyState();
    if (state?.availability === 'Available' && !!state.exportedAt) {
      this.recoveryKeySatisfiedAtEntry.set(true);
    }
  }
}
