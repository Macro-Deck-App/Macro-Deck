let measuringElement: HTMLElement | null = null;

// A second pass is what a row nested in another row needs: the outer row's shrinking moves the inner one.
const TEXT_FIT_PASSES = 2;

const TEXT_FIT_EPSILON_PX = 0.05;

// More than one because a face's advance widths are not exactly proportional to its size - rounded
// glyph advances make the first estimate a little generous, and a label a per cent too wide gets
// an ellipsis. Each step only measures the copy, so none of them disturbs the layout being fitted.
const TEXT_FIT_STEPS = 3;

export interface TextFit {
  declare(): void;

  measure(scale: number): number;

  apply(available: number): number | undefined;
}

export interface MountedFit {
  element: Element;
  fit: TextFit;
  signature: string;
  lastSize: number | undefined;
  lastWidth: number;
}

export interface TextFitScope {
  keep(prior: MountedFit | null, element: Element, fit: TextFit, signature: string): MountedFit;

  drop(entry: MountedFit): void;

  markDirty(): void;

  enter(): boolean;

  leave(): void;

  settle(): void;
}

function elementWidth(element: Element): number {
  return (element as HTMLElement).clientWidth;
}

// Taken from an ancestor, not the node itself: dividing a node's own painted width by its own layout
// width just gives that layout width back. A tile is laid out at the widget basis and scaled by a
// transform, so a painted rectangle and a laid-out box are in different units - above scale 1 the
// fit believed it had more room than it has and the browser ellipsized a clock to "07:40...".
function contentScale(element: HTMLElement): number {
  for (let at = element.parentElement; at !== null; at = at.parentElement) {
    const layout = at.offsetWidth;
    if (layout <= 0) continue;

    const painted = at.getBoundingClientRect().width;
    if (painted > 0) return painted / layout;
  }

  return 1;
}

// The border box rather than the client width: a client width is a whole number of pixels and the text
// under it is not, so a node would read a hair narrower than its own text and shrink again on every
// repaint - a music player's elapsed time stepping down and back up once a second.
function availableTextWidth(element: HTMLElement, scale: number): number {
  return element.getBoundingClientRect().width / scale;
}

// Deliberately not scrollWidth: an element that clips and ellipsizes reports a scroll width equal to
// its client width, so it always answers "no overflow" and the fit leaves the declared size in place
// while the browser cuts the text off. Only an unconstrained copy can disagree with the element.
function textMeasurer(element: HTMLElement): ((fontSizePx: number) => number) | null {
  const owner = element.ownerDocument;
  const view = owner.defaultView;
  if (view === null) return null;

  if (measuringElement === null || measuringElement.ownerDocument !== owner) {
    measuringElement = owner.createElement('span');
    measuringElement.setAttribute('aria-hidden', 'true');
    measuringElement.style.position = 'absolute';
    measuringElement.style.top = '-9999px';
    measuringElement.style.left = '-9999px';
    measuringElement.style.visibility = 'hidden';
    measuringElement.style.whiteSpace = 'pre';
    owner.body.appendChild(measuringElement);
  }

  const measurer = measuringElement;
  const style = view.getComputedStyle(element);
  measurer.style.fontFamily = style.fontFamily;
  measurer.style.fontWeight = style.fontWeight;
  measurer.style.fontStyle = style.fontStyle;
  measurer.style.fontStretch = style.fontStretch;
  measurer.style.letterSpacing = style.letterSpacing;
  measurer.style.fontVariantNumeric = style.fontVariantNumeric;
  measurer.textContent = element.textContent;

  return (fontSizePx: number): number => {
    measurer.style.fontSize = `${fontSizePx}px`;
    return measurer.getBoundingClientRect().width;
  };
}

function px(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : `${value}px`;
}

// Deliberately not dom-writes' setStyle, twice over: a settle writing its own result through the
// scoped helper would mark the scope dirty again from inside itself, and the bypass leaves
// setStyle's cache blind to font-size - so font-size on a fitted element is written here and
// nowhere else.
function setFontSize(element: HTMLElement, value: string | null): void {
  (element as HTMLElement).style.setProperty('font-size', value === null ? '' : value);
}

export function textFit(
  element: HTMLElement,
  declared: number | undefined,
  minimum: number | undefined,
  onApplied?: (size: number | undefined) => void,
): TextFit {
  return {
    declare(): void {
      setFontSize(element, px(declared));
    },

    measure(scale: number): number {
      return availableTextWidth(element, scale);
    },

    apply(available: number): number | undefined {
      let size = declared;
      const measure = declared === undefined || minimum === undefined || available <= 0
        ? null
        : textMeasurer(element);

      if (measure !== null) {
        for (let step = 0; step < TEXT_FIT_STEPS; step++) {
          const natural = measure(size!);
          if (natural <= available) break;

          const next = Math.max(minimum!, size! * available / natural);
          if (next >= size!) break;
          size = next;
        }
      }

      setFontSize(element, px(size));
      if (onApplied) onApplied(size);
      return size;
    },
  };
}

// Reads the whole scope before writing any of it. Measuring only after every node is back at its
// declared size makes the result a function of the tree and its box alone, so room that comes back
// is read as room rather than as the shrunk state an earlier fit produced. Writing before every fit
// has been read would measure some nodes against a box a sibling has already started shrinking away.
function runSettle(fits: MountedFit[]): void {
  if (fits.length === 0) return;

  const scale = contentScale(fits[0].element as HTMLElement);
  let previous: Array<number | undefined> = [];
  for (let index = 0; index < fits.length; index++) previous.push(fits[index].lastSize);

  for (let pass = 0; pass < TEXT_FIT_PASSES; pass++) {
    for (let index = 0; index < fits.length; index++) fits[index].fit.declare();

    const widths: number[] = [];
    for (let index = 0; index < fits.length; index++) widths.push(fits[index].fit.measure(scale));

    const sizes: Array<number | undefined> = [];
    for (let index = 0; index < fits.length; index++) sizes.push(fits[index].fit.apply(widths[index]));

    let moved = false;
    for (let index = 0; index < fits.length; index++) {
      const before = previous[index];
      const size = sizes[index];
      if (size === undefined || before === undefined
        ? size !== before
        : Math.abs(size - before) > TEXT_FIT_EPSILON_PX) {
        moved = true;
      }
    }
    for (let index = 0; index < fits.length; index++) fits[index].lastSize = sizes[index];
    if (!moved) return;
    previous = sizes;
  }
}

export function createTextFitScope(): TextFitScope {
  const fits: MountedFit[] = [];
  let depth = 0;
  let dirty = false;
  let observer: ResizeObserver | null = null;

  function settleIfDirty(): void {
    if (!dirty) return;
    dirty = false;
    runSettle(fits);
  }

  function checkWidths(): void {
    let moved = false;
    for (let index = 0; index < fits.length; index++) {
      const entry = fits[index];
      const width = elementWidth(entry.element);
      if (Math.abs(entry.lastWidth - width) < 1) continue;
      entry.lastWidth = width;
      moved = true;
    }
    if (moved) {
      dirty = true;
      settleIfDirty();
    }
  }

  function dropEntry(entry: MountedFit): void {
    const at = fits.indexOf(entry);
    if (at >= 0) fits.splice(at, 1);
    if (observer === null) return;
    observer.unobserve(entry.element);
    // Nothing else ever calls disconnect, so the last fit leaving is what has to.
    if (fits.length === 0) {
      observer.disconnect();
      observer = null;
    }
  }

  return {
    keep(prior: MountedFit | null, element: Element, fit: TextFit, signature: string): MountedFit {
      if (prior !== null && prior.element === element) {
        if (prior.signature !== signature) dirty = true;
        prior.fit = fit;
        prior.signature = signature;
        return prior;
      }
      // A node rebuilt onto a fresh element re-enters as new, so the observer follows the element.
      if (prior !== null) dropEntry(prior);

      const entry: MountedFit = {
        element, fit, signature, lastSize: undefined, lastWidth: elementWidth(element),
      };
      fits.push(entry);
      dirty = true;

      // One observer for the whole scope, not one per fit: a browser delivers an initial callback for
      // every newly observed element, so an observer per text node schedules that many whole-tree
      // settles right after every mount. It watches every fit element rather than only the tree's
      // root, because a sibling shrinking, an image finishing its decode or a face landing late all
      // move a text node's own width without moving the root's.
      if (typeof ResizeObserver !== 'undefined') {
        if (observer === null) observer = new ResizeObserver(() => checkWidths());
        observer.observe(element);
      }
      return entry;
    },

    drop: dropEntry,

    markDirty(): void {
      dirty = true;
    },

    enter(): boolean {
      const outermost = depth === 0;
      depth++;
      return outermost;
    },

    leave(): void {
      depth--;
    },

    settle: settleIfDirty,
  };
}
