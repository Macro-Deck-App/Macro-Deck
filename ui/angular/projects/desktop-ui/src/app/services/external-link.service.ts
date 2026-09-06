import { Injectable } from '@angular/core';
import { shellBridge } from '../util/shell-bridge';

@Injectable({ providedIn: 'root' })
export class ExternalLinkService {
  get usesShell(): boolean {
    return typeof shellBridge()?.openExternal === 'function';
  }

  open(url: string): void {
    const openExternal = shellBridge()?.openExternal;
    if (!openExternal) {
      window.open(url, '_blank', 'noopener,noreferrer');
      return;
    }

    try {
      // A rejected bridge call cannot fall back to window.open - the WebView would
      // drop that too - so it is reported and the link stays unopened.
      void Promise.resolve(openExternal(url)).catch((error: unknown) => reportFailure(url, error));
    } catch (error) {
      reportFailure(url, error);
    }
  }
}

function reportFailure(url: string, error: unknown): void {
  console.error(`Could not open ${url} in the browser`, error);
}
