import {
  type LocalizationTranslator,
  type LocalizedText,
  type ReadableStore,
  resolveLocalizedText,
  store,
} from '@macro-deck/runtime';

export interface ExecutionFailure {
  readonly message: string;
}

export class ExecutionFeedback {
  private readonly failureStore = store<ExecutionFailure | null>(null);

  readonly failures: ReadableStore<ExecutionFailure | null> = this.failureStore;

  constructor(
    private readonly localization: LocalizationTranslator,
    private readonly fallbackMessage: () => string,
  ) {}

  report(error?: { message?: LocalizedText }): void {
    // `||`, not `??`: resolveLocalizedText answers '' for text it cannot resolve, and an empty toast
    // says less than the generic one.
    const message = resolveLocalizedText(error?.message, this.localization) || this.fallbackMessage();

    // A fresh object every time - a store notifies nobody when set to the value it already holds, and
    // pressing the same broken tile twice has to say so twice.
    this.failureStore.set({ message: message });
  }
}
