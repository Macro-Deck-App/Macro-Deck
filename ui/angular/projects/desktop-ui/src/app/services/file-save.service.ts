import { Injectable, inject } from '@angular/core';
import { AppStrings } from '@macro-deck/runtime';
import { LocalizationService } from '@shared';
import { downloadFile } from '../util/download-file';
import { shellBridge } from '../util/shell-bridge';

export type FileSaveResult =
  | { status: 'saved'; path?: string; viaDialog: boolean }
  | { status: 'canceled' }
  | { status: 'error'; message: string };

@Injectable({ providedIn: 'root' })
export class FileSaveService {
  private readonly localization = inject(LocalizationService);

  async save(blob: Blob, fileName: string): Promise<FileSaveResult> {
    const shell = shellBridge();
    if (!shell?.saveFile) {
      downloadFile(blob, fileName);
      return { status: 'saved', viaDialog: false };
    }

    try {
      const data = await blob.arrayBuffer();
      const result = await shell.saveFile({ fileName, extensions: fileExtensions(fileName), data });
      if (result.canceled) {
        return { status: 'canceled' };
      }
      if (!result.saved) {
        return {
          status: 'error',
          message: result.error ?? this.localization.translateKey(AppStrings.Errors.FileSave.WriteFailed),
        };
      }
      return { status: 'saved', path: result.path ?? undefined, viaDialog: true };
    } catch (error) {
      return {
        status: 'error',
        message: error instanceof Error
          ? error.message
          : this.localization.translateKey(AppStrings.Errors.FileSave.SaveFailed),
      };
    }
  }
}

export function savedFileDetail(result: { fileName?: string; path?: string }): string | undefined {
  if (result.path) {
    return `Saved to ${result.path}`;
  }
  return result.fileName ? `Downloaded ${result.fileName}` : undefined;
}

function fileExtensions(fileName: string): string[] {
  const dot = fileName.lastIndexOf('.');
  const extension = dot > 0 ? fileName.slice(dot + 1) : '';
  return extension ? [extension] : [];
}
