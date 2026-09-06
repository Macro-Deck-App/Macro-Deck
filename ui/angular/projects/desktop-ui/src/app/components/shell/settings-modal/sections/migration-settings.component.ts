import { ChangeDetectionStrategy, Component, OnInit, ViewChild, inject, signal } from '@angular/core';
import { AppStrings, MigrationSourceDto } from '@macro-deck/runtime';
import { ButtonComponent, ErrorBannerComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../../feedback/empty-state/empty-state.component';
import { MigrationService } from '../../../../services/migration.service';
import { MigrationSourceChoice, MigrationWizardService } from '../../../../services/migration-wizard.service';
import { shellBridge } from '../../../../util/shell-bridge';

@Component({
  selector: 'app-migration-settings',
  standalone: true,
  imports: [ButtonComponent, ErrorBannerComponent, EmptyStateComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './migration-settings.component.html',
  styleUrls: ['./migration-settings.component.scss'],
})
export class MigrationSettingsComponent implements OnInit {
  private readonly migration = inject(MigrationService);
  private readonly wizard = inject(MigrationWizardService);
  private readonly toast = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  @ViewChild('uploadInput') private uploadInput?: { nativeElement: HTMLInputElement };

  protected readonly sources = signal<MigrationSourceDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly isDesktopShell = !!shellBridge()?.showOpenDialog;

  private uploadTarget: MigrationSourceDto | null = null;

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    const outcome = await this.migration.getSources();
    if (outcome.status === 'success') {
      this.sources.set(outcome.sources);
    } else {
      this.error.set(outcome.message);
    }
    this.loading.set(false);
  }

  protected async pickFolder(source: MigrationSourceDto): Promise<void> {
    const path = await this.showOpenDialog({ directory: true });
    if (path) {
      this.wizard.open(this.choiceFor(source, { kind: 'path', path }));
    }
  }

  protected async pickArchive(source: MigrationSourceDto): Promise<void> {
    const path = await this.showOpenDialog({ directory: false, extensions: ['zip'] });
    if (path) {
      this.wizard.open(this.choiceFor(source, { kind: 'path', path }));
    }
  }

  protected startUpload(source: MigrationSourceDto): void {
    this.uploadTarget = source;
    this.uploadInput?.nativeElement.click();
  }

  onUploadFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    const source = this.uploadTarget;
    this.uploadTarget = null;
    if (file && source) {
      this.wizard.open(this.choiceFor(source, { kind: 'file', file }));
    }
  }

  private choiceFor(source: MigrationSourceDto, input: MigrationSourceChoice['input']): MigrationSourceChoice {
    return { sourceId: source.id, sourceName: source.name, input };
  }

  private async showOpenDialog(options: { directory: boolean; extensions?: string[] }): Promise<string | null> {
    const bridge = shellBridge();
    if (!bridge?.showOpenDialog) {
      return null;
    }
    try {
      return await bridge.showOpenDialog(options);
    } catch (error) {
      console.error('Native file picker failed', error);
      this.toast.show(this.localization.translateKey(AppStrings.Errors.Migration.OperationFailed), { variant: 'error' });
      return null;
    }
  }
}
