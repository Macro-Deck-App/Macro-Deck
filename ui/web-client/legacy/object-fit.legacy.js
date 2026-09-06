// `object-fit` polyfill for engines that predate it (iOS 9 Safari, Android 4 stock browser),
// following the `ofi` convention: the CSS down-level pass (css-downlevel.mjs) marks every
// `object-fit:cover` / `object-fit:contain` rule with a `font-family:'-md-object-fit-<fit>'`
// declaration, which survives on engines that drop object-fit outright and is readable back via
// getComputedStyle. This module finds marked <img> elements and paints the image as a background
// instead, which frames it the same way object-fit would.
//
// The swap happens on the <img> element itself, not a wrapper: marked images are stacked and
// crossfaded by the renderer, and a wrapper would break that stacking by moving the image out of
// its flow position.
//
// Ported from the Angular client's legacy build. The one change is `contain`: that client only
// marked `cover`, and this one frames artwork and the logo with `contain` as well.
var TRANSPARENT_PIXEL = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==';
var ORIGINAL_SRC_ATTR = 'data-md-object-fit-src';
var MARKER = '-md-object-fit-';
var FITS = ['cover', 'contain'];

function markedFit(win, img) {
  // The renderer sets object-fit as an inline style, which an engine without the property drops out
  // of the CSSOM entirely - so the marker trick below, which reads a stylesheet rule, cannot see it.
  // It stamps the same value as an attribute for exactly this reason.
  var stamped = img.getAttribute('data-object-fit');
  if (stamped) {
    for (var stampedIndex = 0; stampedIndex < FITS.length; stampedIndex += 1) {
      if (stamped === FITS[stampedIndex]) {
        return FITS[stampedIndex];
      }
    }
  }

  if (!win || typeof win.getComputedStyle !== 'function') {
    return null;
  }
  var style = win.getComputedStyle(img);
  var family = style ? style.fontFamily : '';
  if (!family) {
    return null;
  }
  for (var i = 0; i < FITS.length; i += 1) {
    if (family.indexOf(MARKER + FITS[i]) !== -1) {
      return FITS[i];
    }
  }
  return null;
}

function applyFit(img, fit) {
  var src = img.getAttribute('src');
  if (!src || src === TRANSPARENT_PIXEL) {
    return;
  }
  img.setAttribute(ORIGINAL_SRC_ATTR, src);
  img.style.backgroundImage = 'url(' + src + ')';
  img.style.backgroundSize = fit;
  img.style.backgroundRepeat = 'no-repeat';
  img.style.backgroundPosition = 'center';
  // Triggers a load/error event on the placeholder, which is harmless: the sweep below is a
  // no-op for it once src === TRANSPARENT_PIXEL.
  img.src = TRANSPARENT_PIXEL;
}

function restoreFit(img) {
  var original = img.getAttribute(ORIGINAL_SRC_ATTR);
  if (!original) {
    return;
  }
  img.removeAttribute(ORIGINAL_SRC_ATTR);
  img.style.backgroundImage = '';
  img.style.backgroundSize = '';
  img.style.backgroundRepeat = '';
  img.style.backgroundPosition = '';
  img.src = original;
}

export function installObjectFit(doc) {
  if (!doc || !doc.documentElement || 'objectFit' in doc.documentElement.style) {
    return function uninstall() {};
  }

  var win = doc.defaultView || (typeof window !== 'undefined' ? window : null);

  // The cover art changes on every track (a fresh <img src>), and MutationObserver is not
  // available on the oldest targets this build runs on. Every such change ends in a load or
  // error event, so capture-phase listeners plus one sweep at install cover them without
  // leaving a timer running on a device this build exists for precisely because it is slow.
  function sweep() {
    var images = doc.getElementsByTagName('img');
    for (var i = 0; i < images.length; i += 1) {
      var img = images[i];
      var fit = markedFit(win, img);
      if (fit) {
        applyFit(img, fit);
      } else {
        restoreFit(img);
      }
    }
  }

  function onLoadOrError(event) {
    var target = event && event.target;
    if (target && target.tagName === 'IMG') {
      sweep();
    }
  }

  doc.addEventListener('load', onLoadOrError, true);
  doc.addEventListener('error', onLoadOrError, true);
  sweep();

  return function uninstall() {
    doc.removeEventListener('load', onLoadOrError, true);
    doc.removeEventListener('error', onLoadOrError, true);
  };
}
