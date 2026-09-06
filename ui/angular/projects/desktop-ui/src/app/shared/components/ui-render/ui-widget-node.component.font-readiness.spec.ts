import { WritableSignal, signal } from '@angular/core';
import { EMPTY, Observable } from 'rxjs';

import { ApiService } from '../../transport';
import { FontFaceLoadStatus, FontLoaderService, internalFontFamily } from '../../services/font-loader.service';
import { UiNode, UiComponents, UiComponentProperties } from '@macro-deck/runtime';
import { RenderedTree, el, renderTree } from './ui-widget-render-test-support';

const Types = UiComponents;
const Props = UiComponentProperties;

class FakeFontLoaderService {
  private readonly statuses = new Map<string, WritableSignal<FontFaceLoadStatus>>();

  ensureFace(faceId: string | undefined) {
    if (!faceId) {
      return signal<FontFaceLoadStatus>('ready').asReadonly();
    }
    let status = this.statuses.get(faceId);
    if (!status) {
      status = signal<FontFaceLoadStatus>('loading');
      this.statuses.set(faceId, status);
    }
    return status.asReadonly();
  }

  resolve(faceId: string, outcome: FontFaceLoadStatus): void {
    const status = this.statuses.get(faceId);
    if (!status) throw new Error(`ensureFace('${faceId}') was never called`);
    status.set(outcome);
  }
}

function apiSpy(): jasmine.SpyObj<ApiService> {
  const api = jasmine.createSpyObj<ApiService>('ApiService', ['getFontFileUrl', 'onNotification']);
  api.getFontFileUrl.and.returnValue('http://host/fonts/unused');
  api.onNotification.and.callFake(<T>(): Observable<T> => EMPTY);
  (api as unknown as { connectionStateSignal: () => string }).connectionStateSignal = () => 'disconnected';
  return api;
}

function textTree(faceId: string | undefined): UiNode {
  return {
    id: 'root',
    type: Types.Text,
    properties: faceId
      ? { [Props.Text]: 'Hello', [Props.FontFace]: faceId }
      : { [Props.Text]: 'Hello' },
  };
}

function runEl(rendered: RenderedTree): HTMLElement {
  return el(rendered).querySelector('.widget-text') as HTMLElement;
}

function isRevealed(run: HTMLElement): boolean {
  return run.style.visibility !== 'hidden';
}

describe('shared-ui-widget-node font readiness', () => {
  let fakeFontLoader: FakeFontLoaderService;

  beforeEach(() => {
    fakeFontLoader = new FakeFontLoaderService();
  });

  function providers() {
    return [
      { provide: ApiService, useValue: apiSpy() },
      { provide: FontLoaderService, useValue: fakeFontLoader },
    ];
  }

  it('keeps a run with a loading font face in the DOM but not visible', async () => {
    const rendered = await renderTree(textTree('brand-face-1'), providers());
    const run = runEl(rendered);

    expect(run).withContext('the run must still be in the DOM while its font loads').not.toBeNull();
    expect(run.textContent).toBe('Hello');
    expect(isRevealed(run))
      .withContext('a still-loading face must never let the run flash the fallback font first')
      .toBeFalse();
  });

  it('reveals the run as soon as the font signal resolves, with no extra change-detection call', async () => {
    const rendered = await renderTree(textTree('brand-face-2'), providers());
    const run = runEl(rendered);
    expect(isRevealed(run)).toBeFalse();

    fakeFontLoader.resolve('brand-face-2', 'ready');

    // No fixture.detectChanges()/tick() here on purpose: under zoneless OnPush, the readiness
    // signal itself must be what marks the view dirty (issue #457 finding 7). Only wait for
    // whatever Angular's zoneless scheduler picks up on its own from the signal write.
    await rendered.fixture.whenStable();

    expect(isRevealed(run))
      .withContext('resolving the readiness signal alone must reveal the run')
      .toBeTrue();
  });

  it('reveals the run in the fallback font if the font face fails to load', async () => {
    const rendered = await renderTree(textTree('brand-face-3'), providers());
    const run = runEl(rendered);
    expect(isRevealed(run)).toBeFalse();

    fakeFontLoader.resolve('brand-face-3', 'failed');
    await rendered.fixture.whenStable();

    expect(isRevealed(run))
      .withContext('a failed load must still reveal the run rather than hiding it forever')
      .toBeTrue();
    // The declared family stays on the element, but nothing ever registered it with the browser -
    // so the glyphs come from the fallback stack. Registering under a synthetic family name is
    // exactly what makes that safe (issue #457 finding 6): no installed font can answer to it.
    expect(run.style.fontFamily).toBe(internalFontFamily('brand-face-3'));
  });

  it('renders a run with no fontFace visible immediately, with no async gate at all', async () => {
    const rendered = await renderTree(textTree(undefined), providers());
    const run = runEl(rendered);

    expect(isRevealed(run)).toBeTrue();
  });
});
