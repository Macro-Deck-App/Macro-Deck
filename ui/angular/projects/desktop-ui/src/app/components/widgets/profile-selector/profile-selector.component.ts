import { ChangeDetectionStrategy, Component, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { FileOpenService } from '../../../services';
import { Profile, WIDGET_REFERENCE_GAP, AppStrings } from '@macro-deck/runtime';
import { ProfileService, ToastService, ModalComponent, ButtonComponent, ButtonGroupComponent, ErrorBannerComponent, InputComponent, dismissModal, LocalizationService, TranslatePipe } from '@shared';
import { SelectCaretComponent } from '../../forms/select-caret/select-caret.component';
import { GridSettingsComponent } from '../../grid-settings/grid-settings.component';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { DropdownMenuComponent } from '../../overlay/dropdown-menu/dropdown-menu.component';
import { ArchivePreviewModalComponent } from '../../portable/archive-preview-modal/archive-preview-modal.component';
import { ExportOptionsModalComponent, EXPORT_ICONS_TOGGLE } from '../../portable/export-options-modal/export-options-modal.component';
import { ImportPasswordModalComponent } from '../../portable/import-password-modal/import-password-modal.component';
import { ShellDrop, ShellDropTargetDirective } from '../../shell-drop/shell-drop-target.directive';
import { ArchiveDropKind } from '../../../domain/archive-drop.util';
import { ArchiveImportSession } from '../../../domain/archive-import-session';
import { savedFileDetail } from '../../../services/file-save.service';
import { PortabilityService, PortableExportOptions } from '../../../services/portability.service';

@Component({
  selector: 'app-profile-selector',
  standalone: true,
  imports: [
    FormsModule,
    DropdownMenuComponent,
    ModalComponent,
    ButtonComponent,
    ButtonGroupComponent,
    ConfirmationModalComponent,
    ShellDropTargetDirective,
    ArchivePreviewModalComponent,
    ExportOptionsModalComponent,
    ImportPasswordModalComponent,
    ErrorBannerComponent,
    GridSettingsComponent,
    InputComponent,
    SelectCaretComponent,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './profile-selector.component.html',
  styleUrls: ['./profile-selector.component.scss']
})
export class ProfileSelectorComponent {
  protected readonly profileService = inject(ProfileService);
  private readonly portability = inject(PortabilityService);
  private readonly fileOpen = inject(FileOpenService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  @ViewChild(ModalComponent) private modal?: ModalComponent;
  @ViewChild('profileImportInput') private profileImportInput?: { nativeElement: HTMLInputElement };

  protected readonly dropdownOpen = signal(false);

  protected readonly isCreating = signal(false);
  protected readonly createName = signal('');

  private readonly actionProfile = signal<Profile | null>(null);

  protected readonly DEFAULT_ROWS = 3;
  protected readonly DEFAULT_COLUMNS = 5;
  protected readonly BUILT_IN_SPACING = WIDGET_REFERENCE_GAP;

  protected readonly isEditing = signal(false);
  protected readonly editName = signal('');
  protected readonly editRows = signal(this.DEFAULT_ROWS);
  protected readonly editColumns = signal(this.DEFAULT_COLUMNS);
  protected readonly editBackground = signal('');
  protected readonly editSpacing = signal<number | null>(null);
  protected readonly editBorderRadius = signal<number | null>(null);
  protected readonly editError = signal<string | null>(null);

  protected readonly showDeleteConfirm = signal(false);

  protected readonly deleteMessage = computed(() => {
    const name = this.actionProfile()?.name;
    return name
      ? this.localization.translateKey(AppStrings.Widgets.Profile.DeleteConfirmNamed, { name })
      : this.localization.translateKey(AppStrings.Widgets.Profile.DeleteConfirmGeneric);
  });

  protected readonly showExportModal = signal(false);
  protected readonly exportToggles = [EXPORT_ICONS_TOGGLE];

  protected readonly archiveImport = new ArchiveImportSession(
    this.portability,
    (source, password) => this.portability.importProfile(source, password)
  );

  private readonly exportError = signal<string | null>(null);

  protected readonly portableError = computed(() => this.exportError() ?? this.archiveImport.error());

  private readonly editConstraint = computed(() => this.actionProfile()?.layout.constraint ?? null);

  protected readonly editMaxColumns = computed(() => this.editConstraint()?.maxColumns ?? 12);

  protected readonly editMaxRows = computed(() => this.editConstraint()?.maxRows ?? 8);

  protected readonly editColumnsLocked = computed(() => this.editConstraint()?.columnsLocked ?? false);

  protected readonly editRowsLocked = computed(() => this.editConstraint()?.rowsLocked ?? false);

  protected readonly editSpacingHonoured = computed(() => this.editConstraint()?.widgetSpacingHonoured ?? true);

  protected readonly editCornerRadiusHonoured = computed(() => this.editConstraint()?.cornerRadiusHonoured ?? true);

  protected readonly noEffectNote = computed(() => {
    const deviceName = this.editConstraint()?.deviceName;
    return deviceName
      ? this.localization.translateKey(AppStrings.Widgets.GridSettings.NoEffectOnDevice, { device: deviceName })
      : '';
  });

  protected readonly gridLockNote = computed(() => {
    const constraint = this.editConstraint();
    if (!constraint?.deviceName || (!constraint.rowsLocked && !constraint.columnsLocked)) {
      return '';
    }

    return this.localization.translateKey(AppStrings.Widgets.GridSettings.FixedByDevice,
      { device: constraint.deviceName });
  });

  protected readonly layoutWarning = computed(() => {
    const profile = this.actionProfile();
    const compatibility = profile?.layout.compatibility;
    if (!compatibility || compatibility.status === 'ok') {
      return '';
    }

    if (compatibility.status === 'conflictingDevices') {
      return this.localization.translateKey(AppStrings.Widgets.Profile.LayoutConflictWarning,
        { devices: compatibility.deviceNames.join(', ') });
    }

    const constraint = this.editConstraint();
    return this.localization.translateKey(AppStrings.Widgets.Profile.LayoutExceedsWarning, {
      device: constraint?.deviceName ?? compatibility.deviceNames.join(', '),
      columns: constraint?.columns ?? 0,
      rows: constraint?.rows ?? 0
    });
  });

  protected canModify(profile: Profile): boolean {
    return !profile.isVirtual && !profile.layout.rowsLocked && !profile.layout.columnsLocked;
  }

  protected canDelete(profile: Profile): boolean {
    return this.canModify(profile) && this.profileService.profiles().filter(p => !p.isVirtual).length > 1;
  }

  protected canExport(profile: Profile): boolean {
    return !profile.isVirtual;
  }

  selectProfile(id: string): void {
    this.profileService.selectProfile(id);
    this.dropdownOpen.set(false);
  }

  startCreate(): void {
    this.createName.set('');
    this.isCreating.set(true);
  }

  async confirmCreate(): Promise<void> {
    const name = this.createName().trim() || this.localization.translateKey(AppStrings.Widgets.Profile.NewProfile);
    const result = await this.profileService.createProfile(name);
    if (!result.success) {
      console.error('Failed to create profile:', result.error?.message);
    }
    this.cancelCreate();
  }

  cancelCreate(): void {
    dismissModal(this.modal, () => {
      this.isCreating.set(false);
      this.createName.set('');
    });
  }

  startEdit(profile: Profile): void {
    this.dropdownOpen.set(false);
    this.actionProfile.set(profile);
    this.editName.set(profile.name);
    this.editRows.set(profile.defaultRows);
    this.editColumns.set(profile.defaultColumns);
    this.editBackground.set(profile.defaultBackground ?? '');
    this.editSpacing.set(profile.defaultSpacing);
    this.editBorderRadius.set(profile.defaultBorderRadius);
    this.editError.set(null);
    this.isEditing.set(true);
  }

  async confirmEdit(): Promise<void> {
    const profile = this.actionProfile();
    if (!profile) {
      return;
    }
    this.editError.set(null);
    const result = await this.profileService.updateProfile(profile.id, {
      name: this.editName().trim() || profile.name,
      defaultRows: this.editRows(),
      defaultColumns: this.editColumns(),
      defaultBackgroundColor: this.editBackground().trim() || undefined,
      defaultWidgetSpacing: this.editSpacing() ?? -1,
      defaultWidgetBorderRadius: this.editBorderRadius() ?? -1
    });
    if (!result.success) {
      this.editError.set(result.error?.message || this.localization.translateKey(AppStrings.Widgets.Profile.UpdateFailed));
      return;
    }
    this.cancelEdit();
  }

  cancelEdit(): void {
    dismissModal(this.modal, () => {
      this.isEditing.set(false);
      this.editError.set(null);
    });
  }

  startDelete(profile: Profile): void {
    this.dropdownOpen.set(false);
    this.actionProfile.set(profile);
    this.showDeleteConfirm.set(true);
  }

  async confirmDelete(): Promise<void> {
    const profile = this.actionProfile();
    if (profile) {
      const result = await this.profileService.deleteProfile(profile.id);
      if (!result.success) {
        console.error('Failed to delete profile:', result.error?.message);
      }
    }
    this.showDeleteConfirm.set(false);
  }

  cancelDelete(): void {
    this.showDeleteConfirm.set(false);
  }

  startExport(profile: Profile): void {
    this.dropdownOpen.set(false);
    this.actionProfile.set(profile);
    this.exportError.set(null);
    this.showExportModal.set(true);
  }

  async onExportConfirmed(options: PortableExportOptions): Promise<void> {
    const profile = this.actionProfile();
    this.showExportModal.set(false);
    if (!profile) {
      return;
    }

    const result = await this.portability.exportProfile(profile.id, options);
    if (!result.ok) {
      this.exportError.set(result.error ?? this.localization.translateKey(AppStrings.Widgets.Profile.ExportFailed));
      return;
    }
    if (!result.canceled) {
      this.toasts.show(this.localization.translateKey(AppStrings.Widgets.Profile.ProfileExported), { detail: savedFileDetail(result) });
    }
  }

  cancelExport(): void {
    this.showExportModal.set(false);
  }

  dismissPortableError(): void {
    this.exportError.set(null);
    this.archiveImport.dismissError();
  }

  startImport(): void {
    this.dropdownOpen.set(false);
    this.archiveImport.dismissError();
    this.profileImportInput?.nativeElement.click();
  }

  protected readonly profileDropKinds: readonly ArchiveDropKind[] = ['profile'];

  private readonly claimOpenedProfile = effect(() => {
    if (!this.fileOpen.pending().some(entry => entry.kind === 'profile')) {
      return;
    }

    const opened = this.fileOpen.claim('profile');
    if (opened) {
      void this.archiveImport.beginPath(opened.path);
    }
  });

  async onArchiveDropped(drop: ShellDrop): Promise<void> {
    this.dropdownOpen.set(false);
    this.archiveImport.dismissError();
    await this.archiveImport.beginPath(drop.path);
  }

  async onImportFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    if (file) {
      await this.archiveImport.beginFile(file);
    }
  }
}
