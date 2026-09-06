import { dismissButton, element, strokeIcon } from './dom';

export type ToastKind = 'success' | 'error';

export interface ToastOptions {
  detail?: string;
  kind?: ToastKind;
  durationMs?: number;
}

export interface ToastHostOptions {
  dismissLabel: string;
}

const DEFAULT_DURATION_MS = 5000;

const CHECK = ['M20 6 9 17l-5-5'];
const ALERT = ['M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20', 'M12 8v5', 'M12 16h.01'];

interface Shown {
  id: number;
  element: HTMLElement;
  timer: ReturnType<typeof setTimeout> | null;
}

let container: HTMLElement | null = null;
let dismissLabel = '';
let nextId = 1;
let shown: Shown[] = [];

function host(): HTMLElement {
  if (container === null) {
    container = element('div', 'wc-toasts');
    // One live region for the whole stack: each toast is an addition to it, so a screen reader
    // announces the new one instead of re-reading everything on screen.
    container.setAttribute('role', 'status');
    container.setAttribute('aria-live', 'polite');
  }
  return container;
}

export function mountToastHost(root: HTMLElement, options: ToastHostOptions): void {
  dismissLabel = options.dismissLabel;
  root.appendChild(host());
}

export function showToast(message: string, options?: ToastOptions): number {
  const settings = options === undefined ? {} : options;
  const kind: ToastKind = settings.kind === undefined ? 'success' : settings.kind;
  const id = nextId++;

  const toast = element('div', kind === 'error' ? 'wc-toast wc-toast-error' : 'wc-toast');
  toast.appendChild(strokeIcon('wc-toast-icon', kind === 'error' ? ALERT : CHECK));

  const text = element('div', 'wc-toast-text');
  const line = element('span', 'wc-toast-message');
  line.textContent = message;
  text.appendChild(line);
  if (settings.detail !== undefined && settings.detail !== '') {
    const detail = element('span', 'wc-toast-detail');
    detail.textContent = settings.detail;
    text.appendChild(detail);
  }
  toast.appendChild(text);

  const close = dismissButton('wc-toast-dismiss', 'wc-toast-dismiss-icon', dismissLabel);
  close.addEventListener('click', () => dismissToast(id));
  toast.appendChild(close);

  host().appendChild(toast);

  const duration = settings.durationMs === undefined ? DEFAULT_DURATION_MS : settings.durationMs;
  const entry: Shown = { id: id, element: toast, timer: null };
  if (duration > 0) entry.timer = setTimeout(() => dismissToast(id), duration);
  shown.push(entry);

  return id;
}

export function dismissToast(id: number): void {
  for (let index = 0; index < shown.length; index++) {
    const entry = shown[index];
    if (entry.id !== id) continue;
    if (entry.timer !== null) clearTimeout(entry.timer);
    if (entry.element.parentNode !== null) entry.element.parentNode.removeChild(entry.element);
    shown.splice(index, 1);
    return;
  }
}

export function dismissAllToasts(): void {
  const ids: number[] = [];
  for (let index = 0; index < shown.length; index++) ids.push(shown[index].id);
  for (let index = 0; index < ids.length; index++) dismissToast(ids[index]);
}
