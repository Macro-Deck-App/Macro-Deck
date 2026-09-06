import { signal } from '@angular/core';
import { MigrationSummary } from '@macro-deck/runtime';
import { MigrationPreviewOutcome, MigrationRequest, MigrationService, MigrationSourceInput } from '../services/migration.service';

export type MigrationStep = 'source' | 'credentialChoice' | 'keyPrompt' | 'preview' | 'result';

export type MigrationImportSuccess = (profileIds: string[]) => void;

export class MigrationSession {
  readonly step = signal<MigrationStep>('source');

  readonly summary = signal<MigrationSummary | null>(null);
  readonly credentialSummary = signal<MigrationSummary | null>(null);
  readonly keyInvalid = signal(false);

  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly result = signal<{ summary: MigrationSummary; profileIds: string[] } | null>(null);

  private sourceId = '';
  private input: MigrationSourceInput | null = null;
  private decryptionKey: string | undefined;
  private skipDecryption = false;

  constructor(
    private readonly migration: MigrationService,
    private readonly onImported?: MigrationImportSuccess
  ) {}

  async preview(sourceId: string, input: MigrationSourceInput): Promise<void> {
    this.sourceId = sourceId;
    this.input = input;
    this.decryptionKey = undefined;
    this.skipDecryption = false;
    await this.runPreview();
  }

  async chooseSkip(): Promise<void> {
    this.skipDecryption = true;
    this.decryptionKey = undefined;
    this.step.set('source');
    await this.runPreview();
  }

  enterKey(): void {
    this.keyInvalid.set(false);
    this.step.set('keyPrompt');
  }

  async submitKey(key: string): Promise<void> {
    this.decryptionKey = key;
    this.skipDecryption = false;
    await this.runPreview();
  }

  backToCredentialChoice(): void {
    this.keyInvalid.set(false);
    this.step.set('credentialChoice');
  }

  async confirmImport(): Promise<void> {
    this.submitting.set(true);
    this.error.set(null);
    try {
      const outcome = await this.migration.import(this.currentRequest());
      if (outcome.status === 'success') {
        this.result.set({ summary: outcome.summary, profileIds: outcome.profileIds });
        this.step.set('result');
        this.onImported?.(outcome.profileIds);
        return;
      }
      if (outcome.status === 'invalidKey') {
        // Rare (the key ring changed between preview and import), but handled through the exact same
        // door as a rejection at preview time rather than inventing a second, uncovered error path.
        this.keyInvalid.set(true);
        this.step.set('keyPrompt');
        return;
      }
      this.error.set(outcome.message);
    } finally {
      this.submitting.set(false);
    }
  }

  cancel(): void {
    this.reset();
  }

  dismissError(): void {
    this.error.set(null);
  }

  private async runPreview(): Promise<void> {
    this.submitting.set(true);
    this.error.set(null);
    try {
      const outcome = await this.migration.preview(this.currentRequest());
      this.applyPreviewOutcome(outcome);
    } finally {
      this.submitting.set(false);
    }
  }

  private applyPreviewOutcome(outcome: MigrationPreviewOutcome): void {
    if (outcome.status === 'invalidKey') {
      this.keyInvalid.set(true);
      this.step.set('keyPrompt');
      return;
    }
    if (outcome.status === 'error') {
      this.error.set(outcome.message);
      this.step.set('source');
      return;
    }

    const summary = outcome.summary;
    if (summary.credentialStatus === 'KeyRejected') {
      this.keyInvalid.set(true);
      this.step.set('keyPrompt');
      return;
    }
    if (summary.credentialStatus === 'KeyUnavailable') {
      this.credentialSummary.set(summary);
      this.step.set('credentialChoice');
      return;
    }

    this.summary.set(summary);
    this.step.set('preview');
  }

  private currentRequest(): MigrationRequest {
    return {
      sourceId: this.sourceId,
      input: this.input ?? { kind: 'path', path: '' },
      decryptionKey: this.decryptionKey,
      skipDecryption: this.skipDecryption,
    };
  }

  private reset(): void {
    this.step.set('source');
    this.summary.set(null);
    this.credentialSummary.set(null);
    this.keyInvalid.set(false);
    this.error.set(null);
    this.result.set(null);
    this.sourceId = '';
    this.input = null;
    this.decryptionKey = undefined;
    this.skipDecryption = false;
  }
}
