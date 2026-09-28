export type OutdatedUiVariant = 'device' | 'installation';

export interface OutdatedUiText {
  title: string;
  body: string;
  steps: string[];
  action: string;
  versions: string;
}

export interface OutdatedUiOptions {
  variant: OutdatedUiVariant;
  text: (variant: OutdatedUiVariant) => OutdatedUiText;
  onAction: () => void;
  onTextChange?: (listener: () => void) => () => void;
}

export interface OutdatedUiHandle {
  readonly variant: OutdatedUiVariant;
  show(options: OutdatedUiOptions): void;
  remove(): void;
}

export const OUTDATED_UI_ID = 'macro-deck-outdated-ui';

const UI_COMMIT_META = 'macro-deck-ui-commit';
const SERVED_COMMIT_TIMEOUT_MS = 5000;
const SVG_NS = 'http://www.w3.org/2000/svg';

const ICON_PATHS: { [variant: string]: string[] } = {
  device: [
    'M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8',
    'M21 3v5h-5',
    'M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16',
    'M8 16H3v5',
  ],
  installation: [
    'm21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3',
    'M12 9v4',
    'M12 17h.01',
  ],
};

// Written as one attribute so each token keeps a plain declaration in front of it: an engine
// without custom properties drops a var() declaration entirely, fallback argument included.
const STYLES = {
  overlay: [
    'position:fixed', 'top:0', 'right:0', 'bottom:0', 'left:0', 'z-index:2147483647', 'display:flex',
    'align-items:center', 'justify-content:center', 'padding:24px', 'overflow:auto', 'box-sizing:border-box',
    'background:#121212', 'background:var(--color-bg-primary,#121212)',
    'color:#ffffff', 'color:var(--color-text-primary,#ffffff)',
    'font:16px/1.5 -apple-system,BlinkMacSystemFont,Segoe UI,Roboto,sans-serif', 'text-align:center',
  ],
  panel: ['max-width:30rem', 'width:100%'],
  iconBadge: [
    'display:inline-block', 'width:72px', 'height:72px', 'border-radius:36px', 'margin:0 0 20px',
    'line-height:0', 'box-sizing:border-box', 'padding:18px',
  ],
  iconDevice: [
    'background:rgba(33,150,243,0.15)', 'background:var(--color-accent-muted,rgba(33,150,243,0.15))',
    'color:#2196f3', 'color:var(--color-accent,#2196f3)',
  ],
  iconInstallation: [
    'background:rgba(245,158,11,0.15)', 'background:var(--color-warning-muted,rgba(245,158,11,0.15))',
    'color:#f59e0b', 'color:var(--color-warning,#f59e0b)',
  ],
  title: ['margin:0 0 12px', 'font-size:1.375rem', 'line-height:1.3', 'font-weight:600'],
  body: [
    'margin:0 0 20px',
    'color:#a0a0a0', 'color:var(--color-text-secondary,#a0a0a0)',
  ],
  steps: [
    'margin:0 0 24px', 'padding:16px 16px 16px 36px', 'text-align:left', 'border-radius:10px',
    'background:#1a1a1a', 'background:var(--color-bg-secondary,#1a1a1a)',
    'border:1px solid #333333', 'border:1px solid var(--color-border,#333333)',
  ],
  step: ['margin:0 0 8px'],
  lastStep: ['margin:0'],
  button: [
    'display:inline-block', 'min-height:44px', 'padding:10px 24px', 'border:0', 'border-radius:6px',
    'font:inherit', 'font-weight:600', 'cursor:pointer',
    'background:#2196f3', 'background:var(--color-accent,#2196f3)',
    'color:#ffffff', 'color:var(--color-on-accent,#ffffff)',
  ],
  versions: [
    'margin:20px 0 0', 'font-size:0.8125rem',
    'color:#666666', 'color:var(--color-text-muted,#666666)',
  ],
};

function style(declarations: string[]): string {
  return declarations.join(';');
}

function icon(doc: Document, variant: OutdatedUiVariant): Element {
  const svg = doc.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('width', '36');
  svg.setAttribute('height', '36');
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  svg.setAttribute('aria-hidden', 'true');
  const paths = ICON_PATHS[variant];
  for (let index = 0; index < paths.length; index++) {
    const path = doc.createElementNS(SVG_NS, 'path');
    path.setAttribute('d', paths[index]);
    svg.appendChild(path);
  }
  return svg;
}

function styled<K extends keyof HTMLElementTagNameMap>(
  doc: Document,
  tag: K,
  declarations: string[],
): HTMLElementTagNameMap[K] {
  const created = doc.createElement(tag);
  created.setAttribute('style', style(declarations));
  return created;
}

function clear(node: Node): void {
  while (node.firstChild) node.removeChild(node.firstChild);
}

export function readUiCommitFromHtml(html: string): string | null {
  const match = new RegExp('<meta[^>]*name="' + UI_COMMIT_META + '"[^>]*content="([^"]*)"', 'i').exec(html);
  const commit = match ? match[1].replace(/^\s+|\s+$/g, '') : '';
  return commit ? commit : null;
}

export function fetchServedUiCommit(
  baseUri: string,
  nonce: string,
  timeoutMs: number = SERVED_COMMIT_TIMEOUT_MS,
): Promise<string | null> {
  const url = new URL('index.html', baseUri);
  url.searchParams.set('t', nonce);
  const request = fetch(url.toString(), { cache: 'no-store', headers: { Accept: 'text/html' } })
    .then(response => (response.ok ? response.text().then(readUiCommitFromHtml) : null))
    .catch(() => null);
  const timeout = new Promise<null>(resolve => {
    setTimeout(() => resolve(null), timeoutMs);
  });
  return Promise.race([request, timeout]);
}

export function diagnoseOutdatedUi(hostCommit: string, servedCommit: string | null): OutdatedUiVariant {
  if (servedCommit === null) return 'device';
  return servedCommit.toLowerCase() === hostCommit.toLowerCase() ? 'device' : 'installation';
}

interface OpenOverlay {
  doc: Document;
  handle: OutdatedUiHandle;
}

const open: OpenOverlay[] = [];

export function currentOutdatedUi(doc: Document): OutdatedUiHandle | null {
  for (let index = 0; index < open.length; index++) {
    if (open[index].doc === doc) return open[index].handle;
  }
  return null;
}

export function showOutdatedUi(doc: Document, options: OutdatedUiOptions): OutdatedUiHandle {
  const existing = currentOutdatedUi(doc);
  if (existing) {
    existing.show(options);
    return existing;
  }

  const overlay = styled(doc, 'div', STYLES.overlay);
  overlay.id = OUTDATED_UI_ID;
  overlay.setAttribute('role', 'alert');

  const madeInert: HTMLElement[] = [];
  const children = doc.body.children;
  for (let index = 0; index < children.length; index++) {
    const child = children[index] as HTMLElement;
    if (!child.inert) {
      child.inert = true;
      madeInert.push(child);
    }
  }
  doc.body.appendChild(overlay);

  let current = options;
  let unsubscribe: (() => void) | null = null;

  const render = (): void => {
    const text = current.text(current.variant);
    clear(overlay);

    const panel = styled(doc, 'div', STYLES.panel);

    const badge = styled(doc, 'span', STYLES.iconBadge.concat(
      current.variant === 'device' ? STYLES.iconDevice : STYLES.iconInstallation));
    badge.appendChild(icon(doc, current.variant));
    panel.appendChild(badge);

    const title = styled(doc, 'h1', STYLES.title);
    title.textContent = text.title;
    panel.appendChild(title);

    const body = styled(doc, 'p', STYLES.body);
    body.textContent = text.body;
    panel.appendChild(body);

    const steps = styled(doc, 'ol', STYLES.steps);
    for (let index = 0; index < text.steps.length; index++) {
      const step = styled(doc, 'li', index === text.steps.length - 1 ? STYLES.lastStep : STYLES.step);
      step.textContent = text.steps[index];
      steps.appendChild(step);
    }
    panel.appendChild(steps);

    const button = styled(doc, 'button', STYLES.button);
    button.type = 'button';
    button.textContent = text.action;
    button.addEventListener('click', () => current.onAction());
    panel.appendChild(button);

    const versions = styled(doc, 'p', STYLES.versions);
    versions.textContent = text.versions;
    panel.appendChild(versions);

    overlay.appendChild(panel);
  };

  const subscribe = (): void => {
    if (unsubscribe) unsubscribe();
    unsubscribe = current.onTextChange ? current.onTextChange(render) : null;
  };

  const handle: OutdatedUiHandle = {
    get variant() {
      return current.variant;
    },
    show(next: OutdatedUiOptions) {
      const resubscribe = next.onTextChange !== current.onTextChange;
      current = next;
      if (resubscribe) subscribe();
      render();
    },
    remove() {
      if (unsubscribe) {
        unsubscribe();
        unsubscribe = null;
      }
      if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
      for (let index = 0; index < madeInert.length; index++) madeInert[index].inert = false;
      for (let index = 0; index < open.length; index++) {
        if (open[index].handle === handle) {
          open.splice(index, 1);
          break;
        }
      }
    },
  };

  open.push({ doc, handle });
  subscribe();
  render();
  return handle;
}
