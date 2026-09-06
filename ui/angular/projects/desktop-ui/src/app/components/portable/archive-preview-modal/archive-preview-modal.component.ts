import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, computed, inject, signal } from '@angular/core';
import { AppStrings, ArchiveIntegration, ArchiveIntegrationAvailability, ArchiveSummary } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';

interface ContentEntry {
  icon: string;
  label: string;
}

interface IntegrationEntry extends ArchiveIntegration {
  status: string;
  severity: 'ok' | 'warn' | 'error';
}

const AVAILABILITY_KEY: Record<ArchiveIntegrationAvailability, { key: string; severity: 'ok' | 'warn' | 'error' }> = {
  Ready: { key: AppStrings.Portable.Integration.Ready, severity: 'ok' },
  NotConfigured: { key: AppStrings.Portable.Integration.NotConfigured, severity: 'warn' },
  Disabled: { key: AppStrings.Portable.Integration.Disabled, severity: 'warn' },
  Missing: { key: AppStrings.Portable.Integration.Missing, severity: 'error' }
};

const KIND_TITLE_KEY: Record<ArchiveSummary['kind'], string> = {
  Profile: AppStrings.Portable.ArchivePreview.ImportProfileTitle,
  Folder: AppStrings.Portable.ArchivePreview.ImportFolderTitle,
  Widgets: AppStrings.Portable.ArchivePreview.ImportWidgetsTitle
};

const COUNT_KEY = {
  widget: AppStrings.Portable.ArchivePreview.WidgetCount,
  folder: AppStrings.Portable.ArchivePreview.FolderCount,
  icon: AppStrings.Portable.ArchivePreview.IconCount,
  script: AppStrings.Portable.ArchivePreview.ScriptCount,
  variable: AppStrings.Portable.ArchivePreview.VariableCount,
  secret: AppStrings.Portable.ArchivePreview.SecretCount
};

@Component({
  selector: 'shared-archive-preview-modal',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-modal [heading]="title()" size="small" (close)="onCancel()">
      <div class="archive-preview">
        @if (archive(); as summary) {
          <div class="archive-preview-head">
            <p class="archive-preview-name">{{ summary.name || ('macrodeck.app:Portable.ArchivePreview.UnnamedName' | translate) }}</p>
            <p class="archive-preview-meta">{{ 'macrodeck.app:Portable.ArchivePreview.ExportedWith' | translate:{ version: summary.appVersion } }}</p>
          </div>

          <ul class="archive-preview-contents">
            @for (entry of contents(); track entry.label) {
              <li>
                <span class="icon icon-{{ entry.icon }} icon-xs"></span>
                <span>{{ entry.label }}</span>
              </li>
            }
          </ul>

          @if (integrations().length > 0) {
            <div class="archive-preview-section">
              <p class="archive-preview-title">{{ 'macrodeck.app:Portable.ArchivePreview.IntegrationsTitle' | translate }}</p>
              <ul class="archive-preview-integrations">
                @for (integration of integrations(); track integration.id) {
                  <li>
                    <span class="archive-preview-integration-name">{{ integration.name }}</span>
                    <span class="archive-preview-status" [class]="'is-' + integration.severity">
                      {{ integration.status }}
                    </span>
                  </li>
                }
              </ul>
              @if (unsatisfied()) {
                <p class="archive-preview-hint">
                  {{ 'macrodeck.app:Portable.ArchivePreview.IntegrationsHint' | translate }}
                </p>
              }
            </div>
          }

          @if (summary.encrypted) {
            <p class="archive-preview-warning">
              <span class="icon icon-lock icon-xs"></span>
              <span>{{ 'macrodeck.app:Portable.ArchivePreview.EncryptedWarning' | translate }}</span>
            </p>
          }
        }
      </div>
      <div modal-footer>
        <shared-button variant="secondary" (click)="onCancel()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
        <shared-button variant="primary" (click)="onConfirm()">{{ 'macrodeck:Common.Import' | translate }}</shared-button>
      </div>
    </shared-modal>
  `,
  styleUrls: ['./archive-preview-modal.component.scss']
})
export class ArchivePreviewModalComponent {
  private readonly localization = inject(LocalizationService);

  @Input({ required: true })
  set summary(value: ArchiveSummary) {
    this.archive.set(value);
  }

  @Output() confirmed = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  protected readonly archive = signal<ArchiveSummary | null>(null);

  protected readonly title = computed(() => {
    const summary = this.archive();
    const key = summary ? KIND_TITLE_KEY[summary.kind] : AppStrings.Portable.ArchivePreview.ImportDefaultTitle;
    return this.localization.translateKey(key);
  });

  protected readonly contents = computed<ContentEntry[]>(() => {
    const summary = this.archive();
    if (!summary) {
      return [];
    }

    const entries: ContentEntry[] = [
      { icon: 'action-button-type', label: this.plural(summary.widgetCount, COUNT_KEY.widget) }
    ];
    if (summary.kind !== 'Widgets') {
      entries.unshift({ icon: 'folder', label: this.plural(summary.folderCount, COUNT_KEY.folder) });
    }
    if (summary.iconCount > 0) {
      entries.push({ icon: 'image', label: this.plural(summary.iconCount, COUNT_KEY.icon) });
    }
    if (summary.scriptCount > 0) {
      entries.push({ icon: 'file-text', label: this.plural(summary.scriptCount, COUNT_KEY.script) });
    }
    if (summary.variableCount > 0) {
      entries.push({ icon: 'braces-x', label: this.plural(summary.variableCount, COUNT_KEY.variable) });
    }
    if (summary.secretCount > 0) {
      entries.push({ icon: 'lock', label: this.plural(summary.secretCount, COUNT_KEY.secret) });
    }
    return entries;
  });

  protected readonly integrations = computed<IntegrationEntry[]>(() =>
    (this.archive()?.integrations ?? []).map(integration => {
      const { key, severity } = AVAILABILITY_KEY[integration.availability];
      return {
        ...integration,
        status: this.localization.translateKey(key),
        severity
      };
    })
  );

  protected readonly unsatisfied = computed(() =>
    this.integrations().some(integration => integration.severity !== 'ok')
  );

  protected onConfirm(): void {
    dismissModal(this.modal, () => this.confirmed.emit());
  }

  protected onCancel(): void {
    dismissModal(this.modal, () => this.cancelled.emit());
  }

  private plural(count: number, key: string): string {
    return this.localization.translateKey(key, { count });
  }
}
