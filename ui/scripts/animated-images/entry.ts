import { renderUiNode } from '../../runtime/src/public-api';
import type { UiNode } from '../../runtime/src/ui-framework/ui-node.interface';
import type { UiRenderHost } from '../../runtime/src/render/ui-render-host';

const TILE = 200;
const sources = new Map<string, string>();
const loads = new Map<string, number>();
let handle: ReturnType<typeof renderUiNode> | null = null;

const host: UiRenderHost = {
  localization: { translate: (_scope, key) => key },
  resourceUrl: resource => (resource ? sources.get(resource.resourceId) ?? null : null),
  now: () => Date.now(),
  culture: () => 'en-US',
  simpleRendering: () => false,
  fontFamily: () => null,
  fontReady: () => true,
  emit: () => undefined,
};

interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

function incoming(image: Element): boolean {
  return image.classList.contains('widget-artwork-incoming') || image.classList.contains('widget-image-incoming');
}

document.addEventListener('load', event => {
  const target = event.target;
  if (target instanceof HTMLImageElement && !loads.has(target.src)) loads.set(target.src, Date.now());
}, true);

const api = {
  register(resourceId: string, base64: string): void {
    const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
    sources.set(resourceId, URL.createObjectURL(new Blob([bytes], { type: 'image/gif' })));
  },

  loadedAt(resourceId: string): number | null {
    const url = sources.get(resourceId);
    return url === undefined ? null : loads.get(url) ?? null;
  },

  render(type: string, properties: Record<string, unknown>): void {
    const tree = { id: 'artwork', type, properties } as UiNode;
    const tile = document.getElementById('tile') as HTMLElement;
    if (handle === null) handle = renderUiNode(tile, tree, { width: TILE, height: TILE }, null, TILE, host);
    else handle.update(tree, { width: TILE, height: TILE }, null);
  },

  settled(): { box: Box; opacity: string; images: number; masked: number; src: string | null } | null {
    const images = Array.from(document.querySelectorAll('#tile img'));
    const settled = images.find(image => !incoming(image)) as HTMLImageElement | undefined;
    if (settled === undefined) return null;
    const rect = settled.getBoundingClientRect();
    const masked = Array.from(document.querySelectorAll<HTMLElement>('#tile *')).filter(element => {
      const style = getComputedStyle(element) as CSSStyleDeclaration & { webkitMaskImage?: string };
      const mask = style.maskImage || style.webkitMaskImage || 'none';
      return mask !== 'none';
    }).length;
    return {
      box: { x: rect.left, y: rect.top, width: rect.width, height: rect.height },
      opacity: getComputedStyle(settled).opacity,
      images: images.length,
      masked,
      src: settled.getAttribute('src'),
    };
  },

  async read(png: string, box: Box): Promise<{ index: number | null; ring: number[] }> {
    const bytes = Uint8Array.from(atob(png), character => character.charCodeAt(0));
    const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    const context = canvas.getContext('2d') as OffscreenCanvasRenderingContext2D;
    context.drawImage(bitmap, 0, 0);
    const cell = box.width / 5;
    const at = (x: number, y: number) => Array.from(context.getImageData(Math.round(x), Math.round(y), 1, 1).data.subarray(0, 3));
    const pixel = (row: number, column: number) => at(box.x + (column + 0.5) * cell, box.y + (row + 0.5) * box.height / 5);
    const distance = (a: number[], b: number[]) => a.reduce((sum, channel, index) => sum + Math.abs(channel - b[index]), 0);
    const ring = pixel(0, 2);
    const behind = at(Math.max(0, box.x - 3), box.y + box.height / 2);
    const lit = (colour: number[]) => distance(colour, ring) < distance(colour, behind);
    const bits: number[] = [];
    for (let row = 1; row < 4; row++) {
      for (let column = 1; column < 4; column++) bits.push(lit(pixel(row, column)) ? 1 : 0);
    }
    const parity = bits.slice(0, 8).reduce((sum, bit) => sum + bit, 0) % 2;
    const index = parity === bits[8] && distance(ring, behind) > 60 ? bits.slice(0, 8).reduce((sum, bit, at) => sum | bit << at, 0) : null;
    return { index, ring };
  },
};

(window as unknown as { animatedArtwork: typeof api }).animatedArtwork = api;
