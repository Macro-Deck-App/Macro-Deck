import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApplicationIdentityKind, AppStrings, FolderFocusRule, GetApplicationFocusCapabilityResponse } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, CheckboxComponent, ErrorBannerComponent, LocalizationService, ModalComponent, ToggleSwitchComponent, TranslatePipe, dismissModal } from '@shared';
import { ComboboxComponent, ComboboxOption } from '../../forms/combobox/combobox.component';
import { SelectComponent, SelectOption } from '../../forms/select/select.component';
import { DeviceService } from '../../../services/device.service';
import { FolderFocusRuleService } from '../../../services/folder-focus-rule.service';
import { FolderService } from '../../../services';

interface FocusRuleDraft {
  ruleId?: string;
  enabled: boolean;
  applicationIdentity: string;
  identityKind: ApplicationIdentityKind;
  deviceId: string;
  returnOnFocusLoss: boolean;
}

@Component({
  selector: 'app-folder-focus-rule-modal',
  standalone: true,
  imports: [
    FormsModule,
    ModalComponent,
    ButtonComponent,
    ComboboxComponent,
    SelectComponent,
    CheckboxComponent,
    ToggleSwitchComponent,
    ErrorBannerComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './folder-focus-rule-modal.component.html',
  styleUrls: ['./folder-focus-rule-modal.component.scss'],
})
export class FolderFocusRuleModalComponent implements OnInit {
  @Input({ required: true }) folderId = '';
  @Output() closed = new EventEmitter<void>();

  private readonly api = inject(ApiService);
  private readonly focusRuleService = inject(FolderFocusRuleService);
  private readonly folderService = inject(FolderService);
  private readonly localization = inject(LocalizationService);
  protected readonly deviceService = inject(DeviceService);

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly identityKindOptions = computed<SelectOption[]>(() => [
    { value: 'BundleId', label: this.localization.translateKey(AppStrings.Widgets.Folder.FocusRule.BundleIdentifier) },
    { value: 'ExecutablePath', label: this.localization.translateKey(AppStrings.Widgets.Folder.FocusRule.ExecutablePath) },
  ]);

  protected readonly folderName = computed(() =>
    this.folderService.getFolderById(this.folderId)?.name
    ?? this.localization.translateKey(AppStrings.Widgets.Folder.FocusRule.FolderNameFallback));
  protected readonly rules = computed(() => this.focusRuleService.rulesForFolder(this.folderId));

  protected readonly capability = signal<GetApplicationFocusCapabilityResponse | null>(null);
  protected readonly capabilityLoading = signal(true);
  protected readonly supported = computed(() => this.capability()?.supported ?? false);
  protected readonly showIdentityKindPicker = computed(() =>
    this.capability()?.preferredIdentityKind === 'BundleId');
  protected readonly editingDisabled = computed(() => this.capabilityLoading() || !this.supported());

  protected readonly editing = signal<FocusRuleDraft | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);

  protected readonly appOptions = signal<ComboboxOption[]>([]);
  protected readonly appOptionsLoading = signal(false);
  private appFilterDebounce: ReturnType<typeof setTimeout> | null = null;

  protected readonly deviceOptions = computed<SelectOption[]>(() =>
    [...this.deviceService.devices()]
      .sort((a, b) => a.name.localeCompare(b.name))
      .map(device => ({
        value: device.id,
        label: device.online
          ? device.name
          : this.localization.translateKey(AppStrings.Widgets.Folder.FocusRule.DeviceOffline, { name: device.name }),
      })));

  async ngOnInit(): Promise<void> {
    void this.focusRuleService.load();
    void this.deviceService.load();
    await this.loadCapability();
  }

  private async loadCapability(): Promise<void> {
    this.capabilityLoading.set(true);
    try {
      this.capability.set(await this.api.getApplicationFocusSupport());
    } catch {
      this.capability.set({
        supported: false,
        unsupportedReason: this.localization.translateKey(AppStrings.Widgets.Folder.FocusRule.LoadCapabilityFailure),
        preferredIdentityKind: 'ExecutablePath',
      });
    } finally {
      this.capabilityLoading.set(false);
    }
  }

  protected startAdd(): void {
    this.saveError.set(null);
    this.appOptions.set([]);
    this.editing.set({
      enabled: true,
      applicationIdentity: '',
      identityKind: this.capability()?.preferredIdentityKind ?? 'ExecutablePath',
      deviceId: '',
      returnOnFocusLoss: false,
    });
  }

  protected startEdit(rule: FolderFocusRule): void {
    this.saveError.set(null);
    this.appOptions.set([]);
    this.editing.set({
      ruleId: rule.ruleId,
      enabled: rule.enabled,
      applicationIdentity: rule.applicationIdentity,
      identityKind: rule.identityKind,
      deviceId: rule.deviceId,
      returnOnFocusLoss: rule.returnOnFocusLoss,
    });
  }

  protected cancelEdit(): void {
    this.editing.set(null);
    this.saveError.set(null);
  }

  protected onAppIdentityChange(value: string): void {
    const draft = this.editing();
    if (!draft) return;
    const matched = this.appOptions().find(option => option.value === value);
    const kind = matched?.metadata?.['identityKind'] as ApplicationIdentityKind | undefined;
    this.editing.set({ ...draft, applicationIdentity: value, identityKind: kind ?? draft.identityKind });
  }

  protected onAppFilterChange(filter: string): void {
    if (this.appFilterDebounce) {
      clearTimeout(this.appFilterDebounce);
    }
    this.appFilterDebounce = setTimeout(() => void this.loadApps(filter), 250);
  }

  private async loadApps(filter: string): Promise<void> {
    this.appOptionsLoading.set(true);
    try {
      const response = await this.api.getRunningApplications(filter || undefined);
      this.appOptions.set(response.applications.map(app => ({
        value: app.identity,
        label: app.label,
        metadata: { identityKind: app.identityKind },
      })));
    } catch {
      this.appOptions.set([]);
    } finally {
      this.appOptionsLoading.set(false);
    }
  }

  protected setIdentityKind(kind: string | number | null): void {
    const draft = this.editing();
    if (!draft || kind === null) return;
    this.editing.set({ ...draft, identityKind: kind as ApplicationIdentityKind });
  }

  protected setDeviceId(deviceId: string | number | null): void {
    const draft = this.editing();
    if (!draft) return;
    this.editing.set({ ...draft, deviceId: deviceId === null ? '' : String(deviceId) });
  }

  protected setReturnOnFocusLoss(value: boolean): void {
    const draft = this.editing();
    if (!draft) return;
    this.editing.set({ ...draft, returnOnFocusLoss: value });
  }

  protected canSave(): boolean {
    const draft = this.editing();
    return !!draft && draft.applicationIdentity.trim().length > 0 && draft.deviceId.length > 0;
  }

  protected async save(): Promise<void> {
    const draft = this.editing();
    if (!draft || !this.canSave()) return;

    this.saving.set(true);
    this.saveError.set(null);
    try {
      const result = await this.focusRuleService.save({
        folderId: this.folderId,
        ruleId: draft.ruleId,
        enabled: draft.enabled,
        applicationIdentity: draft.applicationIdentity.trim(),
        identityKind: draft.identityKind,
        deviceId: draft.deviceId,
        returnOnFocusLoss: draft.returnOnFocusLoss,
      });
      if (!result.success) {
        this.saveError.set(result.error?.message ?? this.localization.translateKey(AppStrings.Widgets.Folder.FocusRule.SaveFailedDefault));
        return;
      }
      this.editing.set(null);
    } finally {
      this.saving.set(false);
    }
  }

  protected async toggleEnabled(rule: FolderFocusRule): Promise<void> {
    this.saveError.set(null);
    const result = await this.focusRuleService.save({
      folderId: rule.folderId,
      ruleId: rule.ruleId,
      enabled: !rule.enabled,
      applicationIdentity: rule.applicationIdentity,
      identityKind: rule.identityKind,
      deviceId: rule.deviceId,
      returnOnFocusLoss: rule.returnOnFocusLoss,
    });
    if (!result.success) {
      this.saveError.set(result.error?.message ?? this.localization.translateKey(AppStrings.Widgets.Folder.FocusRule.UpdateFailedDefault));
    }
  }

  protected async remove(rule: FolderFocusRule): Promise<void> {
    this.saveError.set(null);
    const result = await this.focusRuleService.delete(rule.folderId, rule.ruleId);
    if (!result.success) {
      this.saveError.set(result.error?.message ?? this.localization.translateKey(AppStrings.Widgets.Folder.FocusRule.RemoveFailedDefault));
    }
  }

  protected close(): void {
    dismissModal(this.modal, () => this.closed.emit());
  }
}
