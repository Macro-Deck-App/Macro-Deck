import { createButton, type ButtonVariant } from './button';
import { dismissButton, element, strokeIcon } from './dom';

export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface ToastSource {
  kind: string;
  name: string;
}

export interface ToastProgress {
  processed: number;
  total: number | null;
}

export interface ToastAction {
  label: string;
  variant: ButtonVariant;
  onPress(): void;
}

export interface ToastOptions {
  detail?: string;
  kind?: ToastKind;
  durationMs?: number;
  source?: ToastSource;
  progress?: ToastProgress;
  actions?: ToastAction[];
  dismissible?: boolean;
}

export interface ToastHostOptions {
  dismissLabel: string;
}

const DEFAULT_DURATION_MS = 5000;

const CHECK = ['M20 6 9 17l-5-5'];
const ALERT = ['M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20', 'M12 8v4', 'M12 16h.01'];
const INFO = ['M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20', 'M12 16v-4', 'M12 8h.01'];
const WARNING = [
  'm21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3',
  'M12 9v4',
  'M12 17h.01',
];

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

export function toastLayer(): HTMLElement {
  return host();
}

export function severityIconPaths(kind: ToastKind): string[] {
  switch (kind) {
    case 'error':
      return ALERT;
    case 'info':
      return INFO;
    case 'warning':
      return WARNING;
    default:
      return CHECK;
  }
}

function classFor(kind: ToastKind): string {
  return kind === 'success' ? 'wc-toast' : 'wc-toast wc-toast-' + kind;
}

export function createProgressBar(progress: ToastProgress, label: string): HTMLElement {
  const track = element('div', 'wc-progress');
  track.setAttribute('role', 'progressbar');
  track.setAttribute('aria-label', label);

  const fill = element('div', 'wc-progress-fill');
  const total = progress.total;

  if (total !== null && total > 0) {
    const ratio = Math.max(0, Math.min(1, progress.processed / total));
    track.setAttribute('aria-valuemin', '0');
    track.setAttribute('aria-valuemax', String(total));
    track.setAttribute('aria-valuenow', String(Math.min(progress.processed, total)));
    fill.style.width = Math.round(ratio * 100) + '%';
  } else {
    track.className += ' wc-progress-indeterminate';
  }

  track.appendChild(fill);
  return track;
}

export function createToast(message: string, settings: ToastOptions, close: () => void): HTMLElement {
  const kind: ToastKind = settings.kind === undefined ? 'success' : settings.kind;

  const toast = element('div', classFor(kind));
  toast.appendChild(strokeIcon('wc-toast-icon', severityIconPaths(kind)));

  const text = element('div', 'wc-toast-text');

  if (settings.source !== undefined) {
    const source = element('span', 'wc-toast-source');
    const category = element('span', 'wc-toast-source-kind');
    category.textContent = settings.source.kind;
    source.appendChild(category);
    if (settings.source.name !== '') {
      const name = element('span', 'wc-toast-source-name');
      name.textContent = settings.source.name;
      source.appendChild(name);
    }
    text.appendChild(source);
  }

  const line = element('span', 'wc-toast-message');
  line.textContent = message;
  text.appendChild(line);
  if (settings.detail !== undefined && settings.detail !== '') {
    const detail = element('span', 'wc-toast-detail');
    detail.textContent = settings.detail;
    text.appendChild(detail);
  }

  if (settings.progress !== undefined) text.appendChild(createProgressBar(settings.progress, message));

  if (settings.actions !== undefined && settings.actions.length > 0) {
    const actions = element('div', 'wc-toast-actions');
    for (let index = 0; index < settings.actions.length; index++) {
      const action = settings.actions[index];
      actions.appendChild(createButton({
        label: action.label,
        variant: action.variant,
        onClick: action.onPress,
      }).element);
    }
    text.appendChild(actions);
  }
  toast.appendChild(text);

  if (settings.dismissible !== false) {
    const dismiss = dismissButton('wc-toast-dismiss', 'wc-toast-dismiss-icon', dismissLabel);
    dismiss.addEventListener('click', close);
    toast.appendChild(dismiss);
  }

  return toast;
}

export function showToast(message: string, options?: ToastOptions): number {
  const settings = options === undefined ? {} : options;
  const id = nextId++;

  const toast = createToast(message, settings, () => dismissToast(id));
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
