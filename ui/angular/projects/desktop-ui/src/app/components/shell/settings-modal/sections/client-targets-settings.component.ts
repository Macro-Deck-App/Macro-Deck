import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { AppStrings, LocalizedText, resolveLocalizedText, WebClientTargetDto, WebClientTargetProvisioningStepDto } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, ErrorBannerComponent, InputComponent, LocalizationService, LocalizedTextPipe, SettingsSectionComponent, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { SelectComponent, SelectOption } from '../../../forms/select/select.component';

const TARGET_NAME_KEYS: Record<string, string> = {
  carthing: AppStrings.Settings.ClientTargets.CarThingName,
};

@Component({
  selector: 'app-client-targets-settings',
  standalone: true,
  imports: [
    FormsModule,
    SettingsSectionComponent,
    ButtonComponent,
    InputComponent,
    SelectComponent,
    ErrorBannerComponent,
    EmptyStateComponent,
    TranslatePipe,
    LocalizedTextPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './client-targets-settings.component.html',
  styleUrls: ['./client-targets-settings.component.scss'],
})
export class ClientTargetsSettingsComponent {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  protected readonly appStrings = AppStrings;

  protected readonly loaded = signal(false);
  protected readonly targets = signal<WebClientTargetDto[]>([]);
  protected readonly error = signal<string | null>(null);

  protected readonly activeTargetId = signal<string | null>(null);
  protected readonly step = signal<WebClientTargetProvisioningStepDto | null>(null);
  protected readonly stepMessage = signal<LocalizedText>(null);
  protected readonly complete = signal(false);
  protected readonly busy = signal(false);

  protected readonly fieldValues = signal<Record<string, string>>({});

  protected readonly canContinue = computed(() => this.step()?.canContinue === true && !this.busy());

  constructor() {
    void this.load();

    // Whether a device can be set up is a live property of the ADB subsystem, and it is routinely
    // turned on in the neighbouring section while this one is on screen. Without this the row stays
    // disabled until the modal is reopened, which reads as a broken button.
    this.api.onAdbStateChanged()
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());
  }

  protected targetName(target: WebClientTargetDto): string {
    const key = TARGET_NAME_KEYS[target.targetId];
    return key ? this.localization.translateKey(key) : target.targetId;
  }

  protected async start(target: WebClientTargetDto): Promise<void> {
    this.activeTargetId.set(target.targetId);
    this.complete.set(false);
    this.stepMessage.set(null);
    await this.run(() => this.api.startWebClientTargetProvisioning(target.targetId));
  }

  protected async advance(): Promise<void> {
    const targetId = this.activeTargetId();
    const current = this.step();
    if (!targetId || !current) return;

    await this.run(() => this.api.advanceWebClientTargetProvisioning(targetId, {
      stepId: current.stepId,
      input: this.fieldValues(),
    }));
  }

  protected cancel(): void {
    this.activeTargetId.set(null);
    this.step.set(null);
    this.stepMessage.set(null);
    this.complete.set(false);
  }

  protected setField(fieldId: string, value: string): void {
    this.fieldValues.update(values => ({ ...values, [fieldId]: value }));
  }

  protected choiceOptions(field: WebClientTargetProvisioningStepDto['fields'][number]): SelectOption[] {
    return (field.choices ?? []).map(choice => ({
      value: choice.value,
      label: resolveLocalizedText(choice.label, this.localization) || choice.value,
    }));
  }

  private async load(): Promise<void> {
    try {
      this.targets.set(await this.api.getWebClientTargets());
      this.error.set(null);
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.ClientTargets.Unavailable));
    } finally {
      this.loaded.set(true);
    }
  }

  private async run(action: () => Promise<{
    kind: string;
    step: WebClientTargetProvisioningStepDto | null;
    message: LocalizedText;
  }>): Promise<void> {
    this.busy.set(true);
    try {
      const result = await action();
      this.step.set(result.step);
      this.stepMessage.set(result.kind === 'Error' ? result.message : null);
      this.complete.set(result.kind === 'Complete');
      // Every step arrives with its own fields; seeding from the defaults here is what makes the
      // documented value the starting point without pinning it.
      this.fieldValues.set(Object.fromEntries(
        (result.step?.fields ?? []).map(field => [field.fieldId, field.defaultValue])));
      this.error.set(null);
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Settings.ClientTargets.Unavailable));
    } finally {
      this.busy.set(false);
    }
  }

  protected dismissError(): void {
    this.error.set(null);
  }
}
