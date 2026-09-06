// Minimal Pointer Events shim for engines that predate them (iOS 9 Safari, Android 4 stock
// browser), ported unchanged from the Angular client's legacy build apart from the references in
// these comments: the renderer binds pointerdown/up/cancel/leave exclusively, so without this a tap
// does nothing at all. No `window.PointerEvent` is ever defined here: nothing in the app source
// feature-detects it, and defining it would tell third-party detection that touch-action,
// coalesced events and real pointer capture exist, none of which this shim provides.
// The `typeof window.PointerEvent !== 'function'` capability check lives in the caller, not
// here, so this module can be installed and exercised directly in a real (modern) browser test.

var POINTER_EVENT_FLAGS = {
  pointerdown: [true, true],
  pointerup: [true, true],
  pointermove: [true, true],
  pointerover: [true, false],
  pointerout: [true, false],
  pointerenter: [false, false],
  pointerleave: [false, false],
  pointercancel: [true, false]
};

function arrayIndexOf(list, item) {
  for (var i = 0; i < list.length; i++) {
    if (list[i] === item) return i;
  }
  return -1;
}

function ancestorsOf(el) {
  var chain = [];
  var node = el;
  while (node && node.nodeType === 1) {
    chain.push(node);
    node = node.parentNode;
  }
  return chain;
}

function cloneOpts(opts, relatedTarget) {
  var out = {};
  for (var key in opts) {
    if (Object.prototype.hasOwnProperty.call(opts, key)) {
      out[key] = opts[key];
    }
  }
  out.relatedTarget = relatedTarget || null;
  return out;
}

// `new PointerEvent` is exactly what these engines lack; `new MouseEvent` does not exist either
// on the Android 4 stock browser (also in .browserslistrc), and a plain `Event` cannot reliably
// carry `clientX`/`button` because those are prototype getters on MouseEvent. createEvent() /
// initMouseEvent() are old runtime APIs that Babel and terser leave untouched, and the resulting
// object is a real `instanceof MouseEvent` - matching the real `PointerEvent extends MouseEvent`
// relationship the renderer relies on when it reads clientX/clientY and pointerId off the event.
function createPointerEvent(doc, type, opts) {
  var flags = POINTER_EVENT_FLAGS[type];
  var evt = doc.createEvent('MouseEvents');
  evt.initMouseEvent(
    type,
    flags[0],
    flags[1],
    doc.defaultView || window,
    0,
    opts.screenX || 0,
    opts.screenY || 0,
    opts.clientX || 0,
    opts.clientY || 0,
    !!opts.ctrlKey,
    !!opts.altKey,
    !!opts.shiftKey,
    !!opts.metaKey,
    opts.button || 0,
    opts.relatedTarget || null
  );
  // `buttons` (and, in some engines, the other pointer-specific fields) is a getter-only
  // accessor inherited from MouseEvent.prototype, so a plain assignment throws in strict mode.
  // defineProperty installs an own property that shadows the accessor instead.
  definePointerField(evt, 'pointerId', opts.pointerId);
  definePointerField(evt, 'pointerType', opts.pointerType);
  definePointerField(evt, 'isPrimary', !!opts.isPrimary);
  definePointerField(evt, 'pressure', opts.pressure);
  definePointerField(evt, 'buttons', opts.buttons);
  definePointerField(evt, 'width', 1);
  definePointerField(evt, 'height', 1);
  definePointerField(evt, 'tiltX', 0);
  definePointerField(evt, 'tiltY', 0);
  return evt;
}

function definePointerField(evt, name, value) {
  Object.defineProperty(evt, name, { value: value, writable: true, configurable: true, enumerable: true });
}

function dispatchSynthetic(doc, target, type, opts, relatedTarget) {
  var evt = createPointerEvent(doc, type, cloneOpts(opts, relatedTarget));
  return target.dispatchEvent(evt);
}

// The pointerenter/pointerleave chain: a non-bubbling event dispatched individually on each
// element from the target up to (but excluding) the elements shared with `related`'s ancestor
// chain. Dispatching directly on each element - rather than relying on bubbling, which these
// events don't have - is what makes them reach (pointerleave) template bindings.
function dispatchEnterChain(doc, target, related, opts) {
  var excludeChain = related ? ancestorsOf(related) : [];
  var chain = [];
  var node = target;
  while (node && node.nodeType === 1 && arrayIndexOf(excludeChain, node) === -1) {
    chain.push(node);
    node = node.parentNode;
  }
  for (var i = chain.length - 1; i >= 0; i--) {
    dispatchSynthetic(doc, chain[i], 'pointerenter', opts, related);
  }
}

function dispatchLeaveChain(doc, target, related, opts) {
  var excludeChain = related ? ancestorsOf(related) : [];
  var chain = [];
  var node = target;
  while (node && node.nodeType === 1 && arrayIndexOf(excludeChain, node) === -1) {
    chain.push(node);
    node = node.parentNode;
  }
  for (var j = 0; j < chain.length; j++) {
    dispatchSynthetic(doc, chain[j], 'pointerleave', opts, related);
  }
}

function detectListenerOptionsSupport(doc) {
  var supported = false;
  try {
    var probe = Object.defineProperty({}, 'passive', {
      get: function () {
        supported = true;
        return false;
      }
    });
    var noop = function () { };
    doc.addEventListener('pointer-events-legacy-probe', noop, probe);
    doc.removeEventListener('pointer-events-legacy-probe', noop, probe);
  } catch (e) {
    supported = false;
  }
  return supported;
}

function installCaptureShim(win) {
  var ElementCtor = win.Element;
  if (!ElementCtor || typeof ElementCtor.prototype.setPointerCapture === 'function') {
    return;
  }
  // The runtime's node renderer and widget grid call setPointerCapture unguarded on every
  // pointerdown they capture, so a no-op shim is mandatory here, not merely defensive.
  ElementCtor.prototype.setPointerCapture = function (pointerId) {
    if (!this.__pointerCaptureIds) this.__pointerCaptureIds = {};
    this.__pointerCaptureIds[pointerId] = true;
  };
  ElementCtor.prototype.releasePointerCapture = function (pointerId) {
    if (this.__pointerCaptureIds) delete this.__pointerCaptureIds[pointerId];
  };
  ElementCtor.prototype.hasPointerCapture = function (pointerId) {
    return !!(this.__pointerCaptureIds && this.__pointerCaptureIds[pointerId]);
  };
}

export function installPointerEvents(doc) {
  var win = doc.defaultView || window;
  var addOpts = detectListenerOptionsSupport(doc) ? { capture: true, passive: false } : true;

  // touch.identifier -> { target, pointerId, isPrimary }. This is implicit pointer capture, per
  // spec: real Pointer Events suppress boundary events for a captured pointer, so a finger that
  // slides off a button and lifts still fires onShortPress on a modern client. touchmove below
  // dispatches on this recorded target without hit-testing, and touchend/touchcancel never
  // re-target - deliberately, to match that behaviour rather than "improve" on it.
  var activeTouches = {};
  var lastTouchEnd = null;
  var mouseIsDown = false;

  function activeTouchCount() {
    var n = 0;
    for (var key in activeTouches) {
      if (Object.prototype.hasOwnProperty.call(activeTouches, key)) n++;
    }
    return n;
  }

  function recordTouchEnd(x, y) {
    lastTouchEnd = { time: Date.now(), x: x, y: y };
  }

  // iOS still fires the compat mouse sequence (mousedown/mouseup/click) after a touch gesture
  // whenever nothing prevented the touchstart. Ignoring a mouse event that lands close to, and
  // shortly after, the last touchend is what stops that sequence from double-triggering a press.
  function withinDedupWindow(x, y) {
    if (!lastTouchEnd) return false;
    if (Date.now() - lastTouchEnd.time > 700) return false;
    var dx = x - lastTouchEnd.x;
    var dy = y - lastTouchEnd.y;
    return (dx * dx + dy * dy) <= 625;
  }

  function onTouchStart(e) {
    var touches = e.changedTouches;
    var anyPrevented = false;
    for (var i = 0; i < touches.length; i++) {
      var touch = touches[i];
      var isPrimary = activeTouchCount() === 0;
      var target = touch.target;
      var pointerId = touch.identifier + 2;
      activeTouches[touch.identifier] = { target: target, pointerId: pointerId, isPrimary: isPrimary };

      var opts = {
        clientX: touch.clientX, clientY: touch.clientY,
        screenX: touch.screenX, screenY: touch.screenY,
        ctrlKey: e.ctrlKey, altKey: e.altKey, shiftKey: e.shiftKey, metaKey: e.metaKey,
        button: 0, buttons: 1,
        pointerId: pointerId, pointerType: 'touch', isPrimary: isPrimary, pressure: 0.5
      };

      dispatchSynthetic(doc, target, 'pointerover', opts, null);
      dispatchEnterChain(doc, target, null, opts);
      var accepted = dispatchSynthetic(doc, target, 'pointerdown', opts, null);
      if (!accepted) anyPrevented = true;
    }
    if (anyPrevented) {
      // dispatchEvent() returns false exactly when a handler called preventDefault(); every
      // widget's onPressStart does. Preventing the source touchstart is what makes iOS suppress
      // the whole compat-mouse sequence and the native tap highlight (the real Pointer Events
      // semantic). It also blocks scrolling for this gesture, which is safe here because the
      // web-client shell is overflow:hidden and the deck itself does not scroll - and it only
      // happens when a handler explicitly asked for it via preventDefault().
      e.preventDefault();
    }
  }

  function onTouchMove(e) {
    var touches = e.changedTouches;
    for (var i = 0; i < touches.length; i++) {
      var touch = touches[i];
      var entry = activeTouches[touch.identifier];
      if (!entry) continue;
      var opts = {
        clientX: touch.clientX, clientY: touch.clientY,
        screenX: touch.screenX, screenY: touch.screenY,
        ctrlKey: e.ctrlKey, altKey: e.altKey, shiftKey: e.shiftKey, metaKey: e.metaKey,
        button: 0, buttons: 1,
        pointerId: entry.pointerId, pointerType: 'touch', isPrimary: entry.isPrimary, pressure: 0.5
      };
      dispatchSynthetic(doc, entry.target, 'pointermove', opts, null);
    }
  }

  function onTouchEnd(e) {
    var touches = e.changedTouches;
    for (var i = 0; i < touches.length; i++) {
      var touch = touches[i];
      var entry = activeTouches[touch.identifier];
      if (!entry) continue;
      var opts = {
        clientX: touch.clientX, clientY: touch.clientY,
        screenX: touch.screenX, screenY: touch.screenY,
        ctrlKey: e.ctrlKey, altKey: e.altKey, shiftKey: e.shiftKey, metaKey: e.metaKey,
        button: 0, buttons: 0,
        pointerId: entry.pointerId, pointerType: 'touch', isPrimary: entry.isPrimary, pressure: 0
      };
      dispatchSynthetic(doc, entry.target, 'pointerup', opts, null);
      dispatchSynthetic(doc, entry.target, 'pointerout', opts, null);
      dispatchLeaveChain(doc, entry.target, null, opts);
      recordTouchEnd(touch.clientX, touch.clientY);
      delete activeTouches[touch.identifier];
    }
  }

  function onTouchCancel(e) {
    var touches = e.changedTouches;
    for (var i = 0; i < touches.length; i++) {
      var touch = touches[i];
      var entry = activeTouches[touch.identifier];
      if (!entry) continue;
      var opts = {
        clientX: touch.clientX, clientY: touch.clientY,
        screenX: touch.screenX, screenY: touch.screenY,
        ctrlKey: e.ctrlKey, altKey: e.altKey, shiftKey: e.shiftKey, metaKey: e.metaKey,
        button: 0, buttons: 0,
        pointerId: entry.pointerId, pointerType: 'touch', isPrimary: entry.isPrimary, pressure: 0
      };
      dispatchSynthetic(doc, entry.target, 'pointercancel', opts, null);
      dispatchSynthetic(doc, entry.target, 'pointerout', opts, null);
      dispatchLeaveChain(doc, entry.target, null, opts);
      recordTouchEnd(touch.clientX, touch.clientY);
      delete activeTouches[touch.identifier];
    }
  }

  function mouseOpts(e, buttons, pressure) {
    return {
      clientX: e.clientX, clientY: e.clientY,
      screenX: e.screenX, screenY: e.screenY,
      ctrlKey: e.ctrlKey, altKey: e.altKey, shiftKey: e.shiftKey, metaKey: e.metaKey,
      button: e.button || 0, buttons: buttons,
      pointerId: 1, pointerType: 'mouse', isPrimary: true, pressure: pressure
    };
  }

  function onMouseDown(e) {
    if (withinDedupWindow(e.clientX, e.clientY)) return;
    mouseIsDown = true;
    dispatchSynthetic(doc, e.target, 'pointerdown', mouseOpts(e, 1, 0.5), null);
  }

  function onMouseMove(e) {
    if (withinDedupWindow(e.clientX, e.clientY)) return;
    dispatchSynthetic(doc, e.target, 'pointermove', mouseOpts(e, mouseIsDown ? 1 : 0, mouseIsDown ? 0.5 : 0), null);
  }

  function onMouseUp(e) {
    if (withinDedupWindow(e.clientX, e.clientY)) return;
    mouseIsDown = false;
    dispatchSynthetic(doc, e.target, 'pointerup', mouseOpts(e, 0, 0), null);
  }

  function onMouseOver(e) {
    if (withinDedupWindow(e.clientX, e.clientY)) return;
    var opts = mouseOpts(e, mouseIsDown ? 1 : 0, mouseIsDown ? 0.5 : 0);
    dispatchSynthetic(doc, e.target, 'pointerover', opts, e.relatedTarget);
    dispatchEnterChain(doc, e.target, e.relatedTarget, opts);
  }

  function onMouseOut(e) {
    if (withinDedupWindow(e.clientX, e.clientY)) return;
    var opts = mouseOpts(e, mouseIsDown ? 1 : 0, mouseIsDown ? 0.5 : 0);
    dispatchSynthetic(doc, e.target, 'pointerout', opts, e.relatedTarget);
    dispatchLeaveChain(doc, e.target, e.relatedTarget, opts);
  }

  installCaptureShim(win);

  var listeners = [
    ['touchstart', onTouchStart],
    ['touchmove', onTouchMove],
    ['touchend', onTouchEnd],
    ['touchcancel', onTouchCancel],
    ['mousedown', onMouseDown],
    ['mousemove', onMouseMove],
    ['mouseup', onMouseUp],
    ['mouseover', onMouseOver],
    ['mouseout', onMouseOut]
  ];

  for (var i = 0; i < listeners.length; i++) {
    doc.addEventListener(listeners[i][0], listeners[i][1], addOpts);
  }

  return function uninstall() {
    for (var j = 0; j < listeners.length; j++) {
      doc.removeEventListener(listeners[j][0], listeners[j][1], addOpts);
    }
  };
}
