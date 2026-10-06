import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { AppStrings, UserFont, UserFontImportStatus } from '@macro-deck/runtime';
import { ButtonComponent, LocalizationService, ToastService, TranslatePipe } from '@shared';
import { EmptyStateComponent } from '../../feedback/empty-state/empty-state.component';
import { FileDropDirective } from '../../file-drop/file-drop.directive';
import { ConfirmationModalComponent } from '../../overlay/confirmation-modal/confirmation-modal.component';
import { FontLoaderService, internalFontFamily } from '../../../shared/services/font-loader.service';
import { UserFontService } from '../../../services/user-font.service';

interface FontFamilyGroup {
  family: string;
  faces: UserFont[];
}

const SLANT_ORDER: Record<UserFont['slant'], number> = { upright: 0, italic: 1, oblique: 2 };

const STATUS_KEY: Record<Exclude<UserFontImportStatus, 'Imported'>, string> = {
  AlreadyPresent: AppStrings.Library.Fonts.Status.AlreadyPresent,
  UnsupportedFormat: AppStrings.Library.Fonts.Status.UnsupportedFormat,
  TooLarge: AppStrings.Library.Fonts.Status.TooLarge,
  InvalidFont: AppStrings.Library.Fonts.Status.InvalidFont,
  AlreadyInstalled: AppStrings.Library.Fonts.Status.AlreadyInstalled,
  AlreadyImported: AppStrings.Library.Fonts.Status.AlreadyImported,
};

@Component({
  selector: 'app-fonts-library-page',
  standalone: true,
  imports: [ButtonComponent, ConfirmationModalComponent, EmptyStateComponent, FileDropDirective, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './fonts-library-page.component.html',
  styleUrls: ['./fonts-library-page.component.scss'],
})
export class FontsLibraryPageComponent implements OnInit {
  protected readonly userFonts = inject(UserFontService);
  private readonly fontLoader = inject(FontLoaderService);
  private readonly toasts = inject(ToastService);
  private readonly localization = inject(LocalizationService);

  @ViewChild('fileInput') private fileInput?: ElementRef<HTMLInputElement>;

  protected readonly isImporting = signal(false);
  protected readonly fontPendingRemoval = signal<UserFont | null>(null);

  protected readonly groups = computed<FontFamilyGroup[]>(() => {
    const byFamily = new Map<string, UserFont[]>();
    for (const font of this.userFonts.fonts()) {
      const list = byFamily.get(font.family);
      if (list) {
        list.push(font);
      } else {
        byFamily.set(font.family, [font]);
      }
    }
    return [...byFamily.entries()]
      .map(([family, faces]) => ({
        family,
        faces: faces.slice().sort((a, b) => a.weight - b.weight || SLANT_ORDER[a.slant] - SLANT_ORDER[b.slant]),
      }))
      .sort((a, b) => a.family.localeCompare(b.family));
  });

  constructor() {
    effect(() => {
      const faceIds = this.userFonts.fonts().map(font => font.faceId).filter(faceId => faceId !== '');
      untracked(() => faceIds.forEach(faceId => this.fontLoader.ensureFace(faceId)));
    });
  }

  ngOnInit(): void {
    void this.userFonts.load();
  }

  protected sampleFamily(font: UserFont): string {
    return `"${internalFontFamily(font.faceId)}", var(--font-sans)`;
  }

  protected faceName(font: UserFont): string {
    return `${font.family} ${font.styleName}`;
  }

  protected removeLabel(font: UserFont): string {
    return this.localization.translateKey(AppStrings.Library.Fonts.RemoveAriaLabel, { name: this.faceName(font) });
  }

  protected removeMessage(font: UserFont): string {
    return this.localization.translateKey(AppStrings.Library.Fonts.RemoveConfirmMessage, { name: this.faceName(font) });
  }

  protected pickFiles(): void {
    this.fileInput?.nativeElement.click();
  }

  protected async onFilesPicked(input: HTMLInputElement): Promise<void> {
    const files = Array.from(input.files ?? []);
    input.value = '';
    await this.importFiles(files);
  }

  protected async onFilesDropped(files: File[]): Promise<void> {
    await this.importFiles(files);
  }

  protected async confirmRemove(): Promise<void> {
    const font = this.fontPendingRemoval();
    this.fontPendingRemoval.set(null);
    if (!font) {
      return;
    }

    if (!(await this.userFonts.remove(font.fontId))) {
      this.toasts.show(this.localization.translateKey(AppStrings.Library.Fonts.RemoveFailed), { variant: 'error' });
    }
  }

  private async importFiles(files: File[]): Promise<void> {
    if (files.length === 0 || this.isImporting()) {
      return;
    }

    this.isImporting.set(true);
    try {
      const outcome = await this.userFonts.import(files);
      if (!outcome.success) {
        this.toasts.show(this.localization.translateKey(AppStrings.Library.Fonts.ImportFailed), { variant: 'error' });
        return;
      }

      const importedCount = outcome.results.filter(result => result.status === 'Imported').length;
      if (importedCount > 0) {
        this.toasts.show(this.localization.translateKey(AppStrings.Library.Fonts.Imported, { count: importedCount }));
      }
      for (const result of outcome.results) {
        if (result.status === 'Imported') {
          continue;
        }
        this.toasts.show(
          this.localization.translateKey(STATUS_KEY[result.status], { fileName: result.fileName }),
          { variant: result.status === 'AlreadyPresent' ? 'success' : 'error' },
        );
      }
    } finally {
      this.isImporting.set(false);
    }
  }
}
