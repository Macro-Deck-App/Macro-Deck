import { Injectable } from '@angular/core';
import { AppStrings, LocalizationTranslator } from '@macro-deck/runtime';
import { selectAllText } from '../util/select-all-text';

export type ClipboardCopyFailureReason = 'insecure-context' | 'unsupported' | 'denied';

export type ClipboardCopyResult =
  | { status: 'copied'; via: 'clipboard-api' | 'exec-command' }
  | { status: 'failed'; reason: ClipboardCopyFailureReason };

@Injectable({ providedIn: 'root' })
export class TextClipboardService {
  async copyText(text: string): Promise<ClipboardCopyResult> {
    const hasClipboardApi = window.isSecureContext === true && typeof navigator.clipboard?.writeText === 'function';

    if (!hasClipboardApi) {
      if (execCommandCopy(text)) {
        return { status: 'copied', via: 'exec-command' };
      }
      return { status: 'failed', reason: window.isSecureContext ? 'unsupported' : 'insecure-context' };
    }

    try {
      await navigator.clipboard.writeText(text);
      return { status: 'copied', via: 'clipboard-api' };
    } catch {
      if (execCommandCopy(text)) {
        return { status: 'copied', via: 'exec-command' };
      }
      return { status: 'failed', reason: 'denied' };
    }
  }
}

export function clipboardFailureDetail(
  reason: ClipboardCopyFailureReason,
  localization?: LocalizationTranslator,
): string {
  const key = (() => {
    switch (reason) {
      case 'insecure-context':
        return AppStrings.Errors.Clipboard.InsecureContext;
      case 'unsupported':
        return AppStrings.Errors.Clipboard.Unsupported;
      case 'denied':
        return AppStrings.Errors.Clipboard.Denied;
    }
  })();
  if (!localization) {
    switch (reason) {
      case 'insecure-context':
        return 'Your browser only allows copying on a secure (HTTPS) connection.';
      case 'unsupported':
        return 'Your browser does not allow this page to copy to the clipboard.';
      case 'denied':
        return 'Clipboard access was denied by your browser.';
    }
  }
  const separator = key.indexOf(':');
  return localization.translate(key.slice(0, separator), key.slice(separator + 1));
}

function execCommandCopy(text: string): boolean {
  if (typeof document.execCommand !== 'function') {
    return false;
  }

  const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  const selection = document.getSelection();
  const previousRange = selection && selection.rangeCount > 0 ? selection.getRangeAt(0).cloneRange() : null;

  const textarea = document.createElement('textarea');
  textarea.value = text;
  textarea.setAttribute('readonly', '');
  textarea.setAttribute('aria-hidden', 'true');
  textarea.setAttribute('tabindex', '-1');
  Object.assign(textarea.style, {
    position: 'fixed',
    top: '0',
    left: '0',
    width: '2em',
    height: '2em',
    padding: '0',
    border: 'none',
    outline: 'none',
    boxShadow: 'none',
    background: 'transparent',
    fontSize: '16px', // Prevents iOS from zooming in on focus.
  });

  document.body.appendChild(textarea);

  try {
    selectAllText(textarea);
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    textarea.remove();
    selection?.removeAllRanges();
    if (previousRange) {
      selection?.addRange(previousRange);
    }
    previousFocus?.focus();
  }
}
