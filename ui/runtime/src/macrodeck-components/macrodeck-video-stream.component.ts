import { UiMacroDeckComponents } from './macrodeck-component-types';
import { nodeLength, resolveLength } from '../ui-framework/length';
import { nodeRaw } from '../ui-framework/node-properties.util';
import { UiComponentProperties } from '../ui-components/component-properties';
import { buttonFit } from '../ui-components/style';
import type { UiComponentDefinition } from '../ui-framework/component-registry';
import type { UiNode } from '../ui-framework/ui-node.interface';
import { VideoStreamReference, VideoStreamView } from '../video-streams/video-stream-view';

interface VideoStreamState {
  view: VideoStreamView | null;
  still: HTMLImageElement | null;
}

export function nodeVideoStreamReference(node: UiNode | null | undefined): VideoStreamReference | null {
  const raw = nodeRaw(node, UiComponentProperties.Stream);
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return null;
  const candidate = raw as { provider?: unknown; id?: unknown };
  if (typeof candidate.provider !== 'string' || candidate.provider.length === 0
    || typeof candidate.id !== 'string' || candidate.id.length === 0) {
    return null;
  }
  return { provider: candidate.provider, id: candidate.id };
}

function paintStill(state: VideoStreamState, element: HTMLElement, src: string, fit: 'contain' | 'cover'): void {
  if (state.view !== null) {
    state.view.dispose();
    state.view.root.remove();
    state.view = null;
  }
  let image = state.still;
  if (image === null) {
    image = element.ownerDocument.createElement('img');
    image.className = 'widget-video-stream-still';
    image.alt = '';
    element.appendChild(image);
    state.still = image;
  }
  if (image.getAttribute('src') !== src) image.src = src;
  image.style.objectFit = fit;
}

export const macrodeckVideoStreamComponent: UiComponentDefinition<VideoStreamState> = {
  type: UiMacroDeckComponents.VideoStream,

  create(doc: Document) {
    return doc.createElement('div');
  },

  createState(): VideoStreamState {
    return { view: null, still: null };
  },

  paint(node, ctx) {
    const element = ctx.element as HTMLElement;
    const extent = resolveLength(nodeLength(node, UiComponentProperties.Size), ctx.basis, ctx.crossExtent) ?? 0;
    const box = { width: ctx.box.width ?? extent, height: ctx.box.height ?? extent };
    ctx.setClassName(element, 'widget-video-stream-host');
    ctx.sizeTo(element, box);

    const still = ctx.host.videoStreamStill?.() ?? null;
    if (still !== null) {
      paintStill(ctx.state, element, still, buttonFit(node) === 'cover' ? 'cover' : 'contain');
      return;
    }

    let view = ctx.state.view;
    if (view === null) {
      view = new VideoStreamView(element.ownerDocument, ctx.host.localization);
      element.appendChild(view.root);
      ctx.state.view = view;
    }

    view.update(
      ctx.host.videoStreams?.() ?? null,
      nodeVideoStreamReference(node),
      buttonFit(node) === 'cover' ? 'cover' : 'contain',
      box,
      ctx.host.localization,
    );
  },

  release(ctx) {
    ctx.state.view?.dispose();
    ctx.state.view = null;
    ctx.state.still?.remove();
    ctx.state.still = null;
  },

  intrinsicMainPx(node, m) {
    return resolveLength(nodeLength(node, UiComponentProperties.Size), m.basis, m.crossExtent) ?? 0;
  },
};
