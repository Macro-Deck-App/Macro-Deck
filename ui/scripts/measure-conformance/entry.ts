import { bundledTranslator, renderUiNode } from '../../runtime/src/public-api';
import { naturalTextWidth } from '../../runtime/src/render/text-fit';

interface MeasureCase {
  tile: { width: number; height: number };
  basis: number;
  locale: string;
  root: Parameters<typeof renderUiNode>[1];
}

const FONT_FAMILY = 'MacroDeckMeasure';
const WEIGHTS = [400, 500, 600, 700];
const FIXED_NOW = Date.UTC(2026, 8, 12, 14, 5, 30);

function round(value: number): number {
  return Math.round(value * 100) / 100;
}

function textsOf(node: HTMLElement): HTMLElement[] {
  const candidates = [node, ...Array.from(node.querySelectorAll<HTMLElement>('.widget-text'))];
  return candidates.filter(text => text.matches('.widget-text') && text.closest('[data-node-id]') === node);
}

// Not scrollHeight: it counts the face's ascent and descent beyond the line box, so a single line in a
// face with tall metrics would read as cut off. An unclamped copy at the same width shows the real lines.
function clippedVertically(text: HTMLElement): boolean {
  const copy = text.cloneNode(true) as HTMLElement;
  copy.removeAttribute('data-node-id');
  copy.style.cssText += `;position:absolute;visibility:hidden;display:block;flex:none;width:${getComputedStyle(text).width};`
    + 'height:auto;min-height:0;max-height:none;-webkit-line-clamp:unset;overflow:visible';
  text.parentElement!.appendChild(copy);
  const natural = parseFloat(getComputedStyle(copy).height);
  copy.remove();
  return natural > parseFloat(getComputedStyle(text).height) + 0.5;
}

function measure(surface: HTMLElement): Record<string, unknown> {
  const origin = surface.getBoundingClientRect();
  const nodes: Record<string, unknown> = {};
  for (const element of Array.from(surface.querySelectorAll<HTMLElement>('[data-node-id]'))) {
    const rect = element.getBoundingClientRect();
    const entry: Record<string, unknown> = {
      type: element.getAttribute('data-node-type'),
      box: {
        x: round(rect.left - origin.left),
        y: round(rect.top - origin.top),
        width: round(rect.width),
        height: round(rect.height),
      },
    };
    const texts = textsOf(element).map(text => ({
      text: text.textContent ?? '',
      naturalWidth: round(naturalTextWidth(text) ?? Number.NaN),
      givenWidth: round(parseFloat(getComputedStyle(text).width)),
      truncated: { x: text.scrollWidth > text.clientWidth, y: clippedVertically(text) },
      // Checked against the node's minimum and dropped before the file is written.
      fontSize: parseFloat(getComputedStyle(text).fontSize),
    }));
    if (texts.length > 0) entry['texts'] = texts;
    nodes[element.getAttribute('data-node-id')!] = entry;
  }
  return nodes;
}

async function main(): Promise<void> {
  try {
    const scene = (await (await fetch('/case.json')).json()) as MeasureCase;

    const face = new FontFace(FONT_FAMILY, 'url(/font.ttf)', { weight: '100 900' });
    document.fonts.add(await face.load());
    await Promise.all(WEIGHTS.map(weight => document.fonts.load(`${weight} 16px ${FONT_FAMILY}`)));
    if (!WEIGHTS.every(weight => document.fonts.check(`${weight} 16px ${FONT_FAMILY}`))) {
      throw new Error('the pinned face did not load');
    }
    document.documentElement.style.setProperty('--font-sans', FONT_FAMILY);

    const tile = document.getElementById('tile') as HTMLElement;
    tile.style.width = `${scene.tile.width}px`;
    tile.style.height = `${scene.tile.height}px`;
    const surface = tile.appendChild(document.createElement('div'));
    surface.style.width = `${scene.tile.width}px`;
    surface.style.height = `${scene.tile.height}px`;

    const host = {
      localization: { translate: (scope: string, key: string, args?: Record<string, unknown>) => bundledTranslator(`${scope}:${key}`, args) },
      resourceUrl: () => null,
      now: () => FIXED_NOW,
      culture: () => scene.locale,
      hourCycle: () => undefined,
      simpleRendering: () => false,
      fontFamily: () => null,
      fontReady: () => true,
      uiFontKey: () => '',
      emit: () => {},
      videoStreams: () => null,
      videoStreamStill: () => null,
    };

    renderUiNode(surface, scene.root, scene.tile, null, scene.basis, host as never);
    await document.fonts.ready;
    await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
    await fetch('/result', { method: 'POST', body: JSON.stringify({ nodes: measure(surface) }) });
  } catch (error) {
    await fetch('/result', { method: 'POST', body: JSON.stringify({ error: String((error as Error)?.stack ?? error) }) });
  }
}

void main();
