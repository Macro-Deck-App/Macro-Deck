import assert from 'node:assert/strict';
import test from 'node:test';
import { JSDOM } from 'jsdom';

import { installObjectFit } from './object-fit.legacy.js';

/**
 * The shim stands in for `object-fit` where the property does not exist. Which images it can see is
 * the whole question: a stylesheet rule survives in the CSSOM as a marker it can read back, but an
 * inline declaration of a property the engine does not know is dropped outright and leaves nothing
 * behind - which is why the renderer stamps the value as an attribute as well.
 */
/**
 * A window without `object-fit`, which is the engine the shim exists for.
 *
 * The property is removed from the prototype rather than the instance, because that is where jsdom
 * declares it and where the shim's `in` check looks - and put back afterwards, because jsdom shares
 * that prototype between every window in the process, so leaving it off would make every later test
 * think it was running on iOS 9 too.
 */
function withoutObjectFit(html, body) {
  const dom = new JSDOM(`<!doctype html><body>${html}</body>`);
  const prototype = Object.getPrototypeOf(dom.window.document.documentElement.style);
  const descriptor = Object.getOwnPropertyDescriptor(prototype, 'objectFit');
  delete prototype.objectFit;
  try {
    body(dom);
  } finally {
    if (descriptor !== undefined) Object.defineProperty(prototype, 'objectFit', descriptor);
  }
}

test('frames an image the renderer stamped, which no stylesheet rule describes', () => {
  withoutObjectFit('<img id="art" src="cover.png" data-object-fit="cover">', dom => {
    installObjectFit(dom.window.document);

    const image = dom.window.document.getElementById('art');
    assert.equal(image.style.backgroundSize, 'cover');
    assert.match(image.style.backgroundImage, /cover\.png/);
    // The <img> itself must stop drawing, or the stretched original shows through the background.
    assert.notEqual(image.getAttribute('src'), 'cover.png');
  });
});

test('honours contain as well as cover, which the client uses for the mark', () => {
  withoutObjectFit('<img id="logo" src="logo.png" data-object-fit="contain">', dom => {
    installObjectFit(dom.window.document);

    assert.equal(dom.window.document.getElementById('logo').style.backgroundSize, 'contain');
  });
});

test('leaves an image alone when nothing asked for a fit', () => {
  withoutObjectFit('<img id="plain" src="plain.png">', dom => {
    installObjectFit(dom.window.document);

    assert.equal(dom.window.document.getElementById('plain').style.backgroundImage, '');
    assert.equal(dom.window.document.getElementById('plain').getAttribute('src'), 'plain.png');
  });
});

test('ignores a value that is not a fit it knows', () => {
  withoutObjectFit('<img id="odd" src="odd.png" data-object-fit="scale-down">', dom => {
    installObjectFit(dom.window.document);

    assert.equal(dom.window.document.getElementById('odd').style.backgroundImage, '');
  });
});

test('does nothing at all on an engine that has object-fit of its own', () => {
  const dom = new JSDOM('<!doctype html><body><img id="art" src="cover.png" data-object-fit="cover"></body>');
  installObjectFit(dom.window.document);

  assert.equal(dom.window.document.getElementById('art').style.backgroundImage, '');
});
