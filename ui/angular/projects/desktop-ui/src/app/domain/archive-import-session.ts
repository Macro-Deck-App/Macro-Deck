import { signal } from '@angular/core';
import { ArchiveSummary } from '@macro-deck/runtime';
import { ArchiveSource, PortabilityService, PortableImportOutcome, fileSource, pathSource } from '../services/portability.service';

export type ArchiveImportRunner = (source: ArchiveSource, password?: string) => Promise<PortableImportOutcome>;

export type ArchiveImportSuccess = (outcome: Extract<PortableImportOutcome, { status: 'success' }>) => void;

export class ArchiveImportSession {
  readonly summary = signal<ArchiveSummary | null>(null);
  readonly passwordVisible = signal(false);
  readonly passwordInvalid = signal(false);
  readonly error = signal<string | null>(null);

  private source: ArchiveSource | null = null;

  constructor(
    private readonly portability: PortabilityService,
    private readonly run: ArchiveImportRunner,
    private readonly onSuccess?: ArchiveImportSuccess
  ) {}

  async begin(source: ArchiveSource): Promise<void> {
    this.reset();
    const outcome = await this.portability.inspectArchive(source);
    if (outcome.status === 'error') {
      this.error.set(outcome.message);
      return;
    }

    this.source = source;
    this.summary.set(outcome.archive);
  }

  beginFile(file: File): Promise<void> {
    return this.begin(fileSource(file));
  }

  beginPath(path: string): Promise<void> {
    return this.begin(pathSource(path));
  }

  async confirm(): Promise<void> {
    this.summary.set(null);
    await this.execute(undefined);
  }

  async submitPassword(password: string): Promise<void> {
    this.passwordVisible.set(false);
    await this.execute(password);
  }

  cancel(): void {
    this.reset();
  }

  dismissError(): void {
    this.error.set(null);
  }

  private async execute(password: string | undefined): Promise<void> {
    const source = this.source;
    if (!source) {
      return;
    }

    const outcome = await this.run(source, password);
    switch (outcome.status) {
      case 'success':
        this.source = null;
        this.onSuccess?.(outcome);
        break;
      case 'passwordRequired':
        this.passwordInvalid.set(false);
        this.passwordVisible.set(true);
        break;
      case 'invalidPassword':
        this.passwordInvalid.set(true);
        this.passwordVisible.set(true);
        break;
      case 'error':
        this.source = null;
        this.error.set(outcome.message);
        break;
    }
  }

  private reset(): void {
    this.source = null;
    this.summary.set(null);
    this.passwordVisible.set(false);
    this.passwordInvalid.set(false);
    this.error.set(null);
  }
}
