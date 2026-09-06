import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';

import { AppStrings } from '@macro-deck/runtime';
import { ButtonComponent, InputComponent, LocalizationService, TranslatePipe } from '@shared';
import { droppedPathExtension } from '../../../domain/icon-drop.util';
import { ShellFileDropService } from '../../../services/shell-file-drop.service';
import { ShellDroppedPath, shellBridge } from '../../../util/shell-bridge';
import { FileBrowserDialogComponent } from '../file-browser-dialog/file-browser-dialog.component';

export type FilePathKind = 'file' | 'folder' | 'image';

const PLACEHOLDER_KEYS: Record<FilePathKind, string> = {
  file: AppStrings.Forms.FilePathInput.PathToFile,
  folder: AppStrings.Forms.FilePathInput.PathToFolder,
  image: AppStrings.Forms.FilePathInput.PathToImage,
};

@Component({
  selector: 'shared-file-path-input',
  standalone: true,
  imports: [FormsModule, InputComponent, ButtonComponent, FileBrowserDialogComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.fpi-drop-active]': 'dropActive()',
    '[attr.title]': 'dropHint',
  },
  template: `
    <shared-input
      [invalid]="invalid"
      [placeholder]="placeholder || defaultPlaceholder"
      [ngModel]="value"
      (ngModelChange)="setValue($event)" />
    <shared-button variant="secondary" (click)="browse()">
      {{ 'macrodeck.app:Forms.FilePathInput.Browse' | translate }}
    </shared-button>

    @if (showBrowser()) {
      <shared-file-browser-dialog
        [directoriesOnly]="kind === 'folder'"
        [extensions]="extensions"
        [initialPath]="value"
        (picked)="onPicked($event)"
        (close)="showBrowser.set(false)" />
    }
  `,
  styleUrls: ['./file-path-input.component.scss'],
})
export class FilePathInputComponent implements OnInit, OnDestroy {
  @Input() kind: FilePathKind = 'file';
  @Input() extensions?: string[];
  @Input() placeholder = '';
  @Input() invalid = false;

  @Input()
  set value(value: string) {
    this.current.set(value ?? '');
  }

  get value(): string {
    return this.current();
  }

  @Output() valueChange = new EventEmitter<string>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly fileDrop = inject(ShellFileDropService);
  private readonly localization = inject(LocalizationService);

  private readonly current = signal('');
  readonly showBrowser = signal(false);
  readonly dropActive = computed(
    () => this.fileDrop.activeTarget() === this.host.nativeElement,
  );

  private unregisterDrop: (() => void) | null = null;

  get defaultPlaceholder(): string {
    return this.localization.translateKey(PLACEHOLDER_KEYS[this.kind]);
  }

  get dropHint(): string | null {
    if (!this.fileDrop.supported) {
      return null;
    }
    return this.localization.translateKey(
      this.kind === 'folder' ? AppStrings.Forms.FilePathInput.DropFolderHere : AppStrings.Forms.FilePathInput.DropFileHere,
    );
  }

  ngOnInit(): void {
    this.unregisterDrop = this.fileDrop.register(
      this.host.nativeElement,
      paths => paths.filter(path => this.accepts(path)),
      paths => this.setValue(paths[0].path),
    );
  }

  ngOnDestroy(): void {
    this.unregisterDrop?.();
  }

  setValue(value: string | number): void {
    this.current.set(`${value}`);
    this.valueChange.emit(this.current());
  }

  async browse(): Promise<void> {
    const bridge = shellBridge();
    if (bridge?.showOpenDialog) {
      try {
        const path = await bridge.showOpenDialog({
          directory: this.kind === 'folder',
          extensions: this.extensions ?? [],
        });
        if (path) {
          this.setValue(path);
        }
        return;
      } catch (error) {
        // A broken bridge must not leave the button dead: fall through to the
        // host-backed browser instead (issue #122).
        console.error('Native file picker failed, falling back to the in-app browser', error);
      }
    }

    this.showBrowser.set(true);
  }

  onPicked(path: string): void {
    this.showBrowser.set(false);
    this.setValue(path);
  }

  private accepts(path: ShellDroppedPath): boolean {
    if (this.kind === 'folder') {
      return path.directory;
    }
    if (!this.extensions?.length) {
      return !path.directory;
    }

    const extension = droppedPathExtension(path.path);
    return this.extensions.some(candidate => candidate.toLowerCase() === extension);
  }
}
