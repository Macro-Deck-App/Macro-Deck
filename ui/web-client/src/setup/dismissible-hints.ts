const STORAGE_PREFIX = 'md.hint.dismissed.';

export interface HintStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

export class DismissibleHints {
  private readonly dismissed: { [key: string]: boolean } = {};
  private readonly storage: HintStorage | null;

  constructor(storage?: HintStorage | null) {
    this.storage = storage === undefined ? DismissibleHints.defaultStorage() : storage;
  }

  isDismissed(key: string): boolean {
    if (this.dismissed[key] === true) return true;
    if (this.storage === null) return false;
    try {
      const remembered = this.storage.getItem(STORAGE_PREFIX + key) !== null;
      if (remembered) this.dismissed[key] = true;
      return remembered;
    } catch {
      return false;
    }
  }

  dismiss(key: string): void {
    this.dismissed[key] = true;
    if (this.storage === null) return;
    try {
      this.storage.setItem(STORAGE_PREFIX + key, '1');
    } catch {
      // Session-only then: the hint stays put away until this page is left.
    }
  }

  private static defaultStorage(): HintStorage | null {
    try {
      return typeof localStorage === 'undefined' ? null : localStorage;
    } catch {
      return null;
    }
  }
}
