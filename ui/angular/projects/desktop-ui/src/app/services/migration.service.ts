import { Injectable, inject } from '@angular/core';
import {
  ApiError,
  AppStrings,
  MigrationImportResponse,
  MigrationPreviewResponse,
  MigrationRequestBody,
  MigrationSourceDto,
  MigrationSummary,
} from '@macro-deck/runtime';
import { ApiService, LocalizationService, ProfileService } from '@shared';

export type MigrationSourceInput = { kind: 'path'; path: string } | { kind: 'file'; file: File };

export interface MigrationRequest {
  sourceId: string;
  input: MigrationSourceInput;
  decryptionKey?: string;
  skipDecryption: boolean;
}

type MigrationFailureOutcome = { status: 'invalidKey' } | { status: 'error'; message: string };

export type MigrationPreviewOutcome = { status: 'success'; summary: MigrationSummary } | MigrationFailureOutcome;

export type MigrationImportOutcome =
  | { status: 'success'; summary: MigrationSummary; profileIds: string[] }
  | MigrationFailureOutcome;

export type MigrationSourcesOutcome = { status: 'success'; sources: MigrationSourceDto[] } | { status: 'error'; message: string };

@Injectable({ providedIn: 'root' })
export class MigrationService {
  private readonly api = inject(ApiService);
  private readonly localization = inject(LocalizationService);
  private readonly profileService = inject(ProfileService);

  async getSources(): Promise<MigrationSourcesOutcome> {
    try {
      const response = await this.api.getMigrationSources();
      return { status: 'success', sources: response.sources };
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.fallbackMessage()) };
    }
  }

  async preview(request: MigrationRequest): Promise<MigrationPreviewOutcome> {
    try {
      const response = await this.runPreview(request);
      if (response.success && response.summary) {
        return { status: 'success', summary: response.summary };
      }
      return this.mapError(response.error);
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.fallbackMessage()) };
    }
  }

  async import(request: MigrationRequest): Promise<MigrationImportOutcome> {
    try {
      const response = await this.runImport(request);
      if (response.success && response.summary) {
        // The created profiles arrive through ProfileCreatedEvent too, but that only reaches this
        // client's own state on the next load - nudge it now so the profile list is current the
        // moment the wizard reports success.
        await this.profileService.loadProfiles();
        return { status: 'success', summary: response.summary, profileIds: response.profileIds };
      }
      return this.mapError(response.error);
    } catch (error) {
      return { status: 'error', message: errorMessage(error, this.fallbackMessage()) };
    }
  }

  private runPreview(request: MigrationRequest): Promise<MigrationPreviewResponse> {
    return request.input.kind === 'path'
      ? this.api.previewMigration(this.pathBody(request, request.input.path))
      : this.api.previewMigrationUpload(request.input.file, request.sourceId, request.decryptionKey, request.skipDecryption);
  }

  private runImport(request: MigrationRequest): Promise<MigrationImportResponse> {
    return request.input.kind === 'path'
      ? this.api.importMigration(this.pathBody(request, request.input.path))
      : this.api.importMigrationUpload(request.input.file, request.sourceId, request.decryptionKey, request.skipDecryption);
  }

  private pathBody(request: MigrationRequest, path: string): MigrationRequestBody {
    return {
      sourceId: request.sourceId,
      path,
      decryptionKey: request.decryptionKey,
      skipDecryption: request.skipDecryption,
    };
  }

  private mapError(error?: ApiError): MigrationFailureOutcome {
    if (error?.code === 'InvalidDecryptionKey') {
      return { status: 'invalidKey' };
    }
    return { status: 'error', message: error?.message ?? this.fallbackMessage() };
  }

  private fallbackMessage(): string {
    return this.localization.translateKey(AppStrings.Errors.Migration.OperationFailed);
  }
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}
