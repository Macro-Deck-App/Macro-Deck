import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { AppStrings, FilesystemEntry } from '@macro-deck/runtime';
import { ApiService, ButtonComponent, LocalizationService, ModalComponent, TranslatePipe, dismissModal } from '@shared';

@Component({
  selector: 'shared-file-browser-dialog',
  standalone: true,
  imports: [ModalComponent, ButtonComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <shared-modal
      [heading]="directoriesOnly
        ? ('macrodeck.app:Forms.FileBrowser.SelectFolder' | translate)
        : ('macrodeck.app:Forms.FileBrowser.SelectFile' | translate)"
      [showFooter]="true"
      maxWidth="640px"
      [style.--modal-height]="'min(85vh, 42.25rem)'"
      [zIndex]="1100"
      (close)="close.emit()">
      <div class="fb-body">
        <div class="fb-path">
          <shared-button
            variant="ghost"
            size="icon"
            [attr.aria-label]="'macrodeck.app:Forms.FileBrowser.ParentDirectory' | translate"
            [disabled]="!parentPath() && currentPath() !== ''"
            (click)="navigateUp()">
            <svg class="fb-up-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
              stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="m5 12 7-7 7 7"/>
              <path d="M12 19V5"/>
            </svg>
          </shared-button>
          <span class="fb-path-text">{{ currentPath() || ('macrodeck.app:Forms.FileBrowser.Computer' | translate) }}</span>
        </div>

        @if (error()) {
          <div class="fb-error">{{ error() }}</div>
        }

        <div class="fb-list">
          @for (entry of entries(); track entry.path) {
            <button
              type="button"
              class="fb-entry"
              [class.fb-selected]="selectedPath() === entry.path"
              (click)="select(entry)"
              (dblclick)="openEntry(entry)">
              <span class="icon icon-sm" [class.icon-folder]="entry.isDirectory" [class.icon-file]="!entry.isDirectory"></span>
              <span class="fb-entry-name">{{ entry.name }}</span>
            </button>
          } @empty {
            <div class="fb-empty">{{ 'macrodeck.app:Forms.FileBrowser.EmptyDirectory' | translate }}</div>
          }
        </div>
      </div>

      <div modal-footer class="fb-footer">
        <shared-button variant="secondary" (click)="onCancel()">{{ 'macrodeck:Common.Cancel' | translate }}</shared-button>
        <shared-button
          variant="primary"
          [disabled]="!canConfirm()"
          (click)="confirm()">{{ 'macrodeck.app:Forms.FileBrowser.SelectButton' | translate }}</shared-button>
      </div>
    </shared-modal>
  `,
  styleUrls: ['./file-browser-dialog.component.scss'],
})
export class FileBrowserDialogComponent implements OnInit {
  @Input() directoriesOnly = false;
  @Input() extensions?: string[];
  @Input() initialPath?: string;

  @Output() picked = new EventEmitter<string>();
  @Output() close = new EventEmitter<void>();

  @ViewChild(ModalComponent) private modal?: ModalComponent;

  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);

  readonly currentPath = signal('');
  readonly parentPath = signal<string | undefined>(undefined);
  readonly entries = signal<FilesystemEntry[]>([]);
  readonly selectedPath = signal<string | null>(null);
  readonly selectedIsDirectory = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.load(this.initialPath ?? '');
  }

  canConfirm(): boolean {
    if (this.directoriesOnly) {
      return this.selectedIsDirectory() || this.currentPath() !== '';
    }
    return this.selectedPath() !== null && !this.selectedIsDirectory();
  }

  select(entry: FilesystemEntry): void {
    this.selectedPath.set(entry.path);
    this.selectedIsDirectory.set(entry.isDirectory);
  }

  openEntry(entry: FilesystemEntry): void {
    if (entry.isDirectory) {
      void this.load(entry.path);
    } else if (!this.directoriesOnly) {
      dismissModal(this.modal, () => this.picked.emit(entry.path));
    }
  }

  onCancel(): void {
    dismissModal(this.modal, () => this.close.emit());
  }

  navigateUp(): void {
    void this.load(this.parentPath() ?? '');
  }

  confirm(): void {
    const selection = this.directoriesOnly
      ? (this.selectedIsDirectory() ? this.selectedPath() : null) ?? this.currentPath()
      : this.selectedPath();
    if (selection) {
      dismissModal(this.modal, () => this.picked.emit(selection));
    }
  }

  private async load(path: string): Promise<void> {
    this.error.set(null);
    this.selectedPath.set(null);
    this.selectedIsDirectory.set(false);

    try {
      const response = await this.api.getFilesystemEntries({
        path: path || undefined,
        extensions: this.extensions,
        directoriesOnly: this.directoriesOnly,
      });

      if (response.error) {
        this.error.set(response.error.message);
        return;
      }

      this.currentPath.set(response.path);
      this.parentPath.set(response.parentPath);
      this.entries.set(response.entries);
    } catch {
      this.error.set(this.localization.translateKey(AppStrings.Forms.FileBrowser.LoadFailed));
    }
  }
}
