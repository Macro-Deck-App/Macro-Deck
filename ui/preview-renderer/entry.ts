import { asLocalizedRef, bundledTranslator, formatTemplate, isComponentProfileType, pluralForm, renderUiNode } from '../runtime/src/public-api';

interface Scene {
  width: number;
  height: number;
  radius: number;
  theme: 'dark' | 'light';
  background: string;
  locale: string;
  videoStreamImage?: string;
  resources: Record<string, string>;
  translations: Record<string, string>;
  root: Parameters<typeof renderUiNode>[1];
}

const WIDGET_REFERENCE_CELL = 120;
// A fixed clock keeps time and progress components identical on every run.
const FIXED_NOW = Date.UTC(2026, 8, 12, 14, 5, 30);

function translate(translations: Record<string, string>, scope: string, key: string, args?: Record<string, unknown>, depth = 0): string {
  const qualified = `${scope}:${key}`;
  const count = args?.['count'];
  const template = translations[qualified]
    ?? (count === undefined ? undefined : translations[`${qualified}.${pluralForm(count)}`] ?? translations[`${qualified}.Other`]);
  if (template === undefined) return bundledTranslator(qualified, args);

  const resolved: Record<string, unknown> = { ...args };
  for (const name in args) {
    const ref = depth < 4 ? asLocalizedRef(args[name]) : undefined;
    if (ref) resolved[name] = translate(translations, ref.scope, ref.key, ref.arguments, depth + 1);
  }
  return formatTemplate(template, resolved);
}

async function report(id: string, status: string): Promise<void> {
  await fetch(`/status/${id}`, { method: 'POST', body: status });
}

async function main(): Promise<void> {
  const id = new URLSearchParams(location.search).get('id') ?? '';
  try {
    const scene = (await (await fetch(`/scene/${id}.json`)).json()) as Scene;
    document.documentElement.classList.toggle('light', scene.theme === 'light');
    document.documentElement.style.background = scene.background;
    document.body.style.background = scene.background;

    const tile = document.getElementById('tile') as HTMLElement;
    tile.style.width = `${scene.width}px`;
    tile.style.height = `${scene.height}px`;

    // Only the widget profile is drawn by the framework-free runtime; configuration views need the Angular renderer.
    if (!isComponentProfileType(scene.root.type)) {
      await report(id, `unsupported: ${scene.root.type}`);
      return;
    }

    const scale = Math.min(scene.width, scene.height) / WIDGET_REFERENCE_CELL;
    tile.style.borderRadius = `${scene.radius * scale}px`;
    const width = scene.width / scale;
    const height = scene.height / scale;
    const surface = tile.appendChild(document.createElement('div'));
    surface.style.width = `${width}px`;
    surface.style.height = `${height}px`;
    surface.style.transform = `scale(${scale})`;
    surface.style.transformOrigin = '0 0';

    const host = {
      localization: { translate: (scope: string, key: string, args?: Record<string, unknown>) => translate(scene.translations ?? {}, scope, key, args) },
      resourceUrl: (resource?: { resourceId?: string }) => scene.resources[resource?.resourceId ?? ''] ?? null,
      now: () => FIXED_NOW,
      culture: () => scene.locale,
      hourCycle: () => undefined,
      simpleRendering: () => false,
      fontFamily: () => null,
      fontReady: () => true,
      uiFontKey: () => '',
      emit: () => {},
      videoStreams: () => null,
      videoStreamStill: () => scene.videoStreamImage ?? null,
    };

    renderUiNode(surface, scene.root, { width, height }, null, Math.min(width, height), host as never);
    await document.fonts.ready;
    await Promise.all(Array.from(surface.querySelectorAll('img.widget-video-stream-still'), image => (image as HTMLImageElement).decode().catch(() => undefined)));
    await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
    await report(id, 'ready');
  } catch (error) {
    await report(id, `error: ${String((error as Error)?.stack ?? error)}`);
  }
}

void main();
