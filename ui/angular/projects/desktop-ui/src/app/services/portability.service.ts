import { Injectable, inject } from '@angular/core';
import { ApiError, AppStrings, ArchiveSummary } from '@macro-deck/runtime';
import { ApiService, LocalizationService, ProfileService } from '@shared';
import { FileSaveService } from './file-save.service';

export interface PortableExportOptions {
  includeSecrets: boolean;
  password?: string;
  includeIcons?: boolean;
  includeSubfolders?: boolean;
}

export type ArchiveSource = { kind: 'file'; file: File } | { kind: 'path'; path: string };

export function fileSource(file: File): ArchiveSource {
  return { kind: 'file', file };
}

export function pathSource(path: string): ArchiveSource {
  return { kind: 'path', path };
}

export type PortableInspectOutcome =
  | { status: 'success'; archive: ArchiveSummary }
  | { status: 'error'; message: string };

export interface PortableExportResult {
  ok: boolean;
  canceled?: boolean;
  fileName?: string;
  path?: string;
  error?: string;
}

export type PortableImportOutcome =
  | { status: 'success'; count?: number; folderId?: string }
  | { status: 'passwordRequired' }
  | { status: 'invalidPassword' }
  | { status: 'error'; message: string };

@Injectable({ providedIn: 'root' })
export class PortabilityService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly profileService = inject(ProfileService);
  private readonly fileSave = inject(FileSaveService);

  async exportProfile(profileId: string, options: PortableExportOptions): Promise<PortableExportResult> {
    try {
      const { blob, fileName } = await this.api.exportProfile(profileId, this.toRequest(options));
      return await this.saveExport(blob, fileName);
    } catch (error) {
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Portability.OperationFailed)) };
    }
  }

  async importProfile(source: ArchiveSource, password?: string): Promise<PortableImportOutcome> {
    try {
      const response = source.kind === 'file'
        ? await this.api.importProfile(source.file, password)
        : await this.api.importProfileFromPath(source.path, password);
      if (response.success) {
        await this.profileService.loadProfiles();
        if (response.profile) {
          this.profileService.selectProfile(response.profile.id);
        }
        return { status: 'success' };
      }
      return mapImportError(response.error, this.localization);
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Portability.OperationFailed)) };
    }
  }

  async exportFolder(folderId: string, options: PortableExportOptions): Promise<PortableExportResult> {
    try {
      const { blob, fileName } = await this.api.exportFolder(folderId, {
        includeSubfolders: options.includeSubfolders ?? false,
        ...this.toRequest(options),
      });
      return await this.saveExport(blob, fileName);
    } catch (error) {
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Portability.OperationFailed)) };
    }
  }

  async importFolder(
    profileId: string,
    parentFolderId: string | null,
    source: ArchiveSource,
    password?: string
  ): Promise<PortableImportOutcome> {
    try {
      const response = source.kind === 'file'
        ? await this.api.importFolder(profileId, parentFolderId, source.file, password)
        : await this.api.importFolderFromPath(profileId, parentFolderId, source.path, password);
      if (response.success) {
        return { status: 'success', count: response.folderCount, folderId: response.folder?.id };
      }
      return mapImportError(response.error, this.localization);
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Portability.OperationFailed)) };
    }
  }

  async exportWidgets(
    folderId: string,
    widgetIds: string[],
    options: PortableExportOptions
  ): Promise<PortableExportResult> {
    try {
      const { blob, fileName } = await this.api.exportWidgets({ folderId, widgetIds, ...this.toRequest(options) });
      return await this.saveExport(blob, fileName);
    } catch (error) {
      return { ok: false, error: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Portability.OperationFailed)) };
    }
  }

  async importWidgets(
    folderId: string,
    anchorX: number,
    anchorY: number,
    source: ArchiveSource,
    password?: string
  ): Promise<PortableImportOutcome> {
    try {
      const response = source.kind === 'file'
        ? await this.api.importWidgets(folderId, anchorX, anchorY, source.file, password)
        : await this.api.importWidgetsFromPath(folderId, anchorX, anchorY, source.path, password);
      if (response.success) {
        return { status: 'success', count: response.widgets.length };
      }
      return mapImportError(response.error, this.localization);
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Portability.OperationFailed)) };
    }
  }

  private async saveExport(blob: Blob, fileName: string): Promise<PortableExportResult> {
    const result = await this.fileSave.save(blob, fileName);
    if (result.status === 'canceled') {
      return { ok: true, canceled: true, fileName };
    }
    if (result.status === 'error') {
      return { ok: false, error: result.message };
    }
    return { ok: true, fileName, path: result.path };
  }

  async inspectArchive(source: ArchiveSource): Promise<PortableInspectOutcome> {
    try {
      const response = source.kind === 'file'
        ? await this.api.inspectArchive(source.file)
        : await this.api.inspectArchivePath(source.path);
      if (response.success && response.archive) {
        return { status: 'success', archive: response.archive };
      }
      return {
        status: 'error',
        message: response.error?.message ?? this.localization.translateKey(AppStrings.Errors.Portability.InvalidArchive),
      };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.localization.translateKey(AppStrings.Errors.Portability.OperationFailed)) };
    }
  }

  private toRequest(options: PortableExportOptions): {
    includeIcons: boolean;
    includeSecrets: boolean;
    password?: string;
  } {
    return {
      includeIcons: options.includeIcons ?? true,
      includeSecrets: options.includeSecrets,
      password: options.includeSecrets ? options.password : undefined,
    };
  }
}

function mapImportError(error: ApiError | undefined, localization: LocalizationService): PortableImportOutcome {
  if (error?.code === 'PasswordRequired') {
    return { status: 'passwordRequired' };
  }
  if (error?.code === 'InvalidPassword') {
    return { status: 'invalidPassword' };
  }
  return {
    status: 'error',
    message: error?.message ?? localization.translateKey(AppStrings.Errors.Portability.ImportFailed),
  };
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}
