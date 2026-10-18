const OVERFLOW_CLASS = 'widget-text-clamp-overflow';

const SCALE_TOLERANCE = 0.01;

const EMPTY_PROBE_REACH = 8;

interface TextPosition {
  node: Text;
  offset: number;
}

function textNodes(element: HTMLElement): Text[] {
  const nodes: Text[] = [];
  const walker = element.ownerDocument.createTreeWalker(element, NodeFilter.SHOW_TEXT);
  for (let current = walker.nextNode(); current !== null; current = walker.nextNode()) {
    if ((current as Text).length > 0) nodes.push(current as Text);
  }
  return nodes;
}

export function revealClampOverflow(element: HTMLElement): void {
  const hidden = element.querySelectorAll(`.${OVERFLOW_CLASS}`);
  if (hidden.length === 0) return;
  for (let index = 0; index < hidden.length; index++) {
    const part = hidden[index];
    if (part.tagName !== 'SPAN') {
      part.classList.remove(OVERFLOW_CLASS);
      continue;
    }
    const parent = part.parentNode;
    if (parent === null) continue;
    while (part.firstChild) parent.insertBefore(part.firstChild, part);
    parent.removeChild(part);
  }
  element.normalize();
}

function positionAt(nodes: Text[], starts: number[], index: number): TextPosition {
  let at = nodes.length - 1;
  while (at > 0 && starts[at] > index) at--;
  return { node: nodes[at], offset: index - starts[at] };
}

function centreOf(doc: Document, position: TextPosition): number | null {
  const range = doc.createRange();
  range.setStart(position.node, position.offset);
  range.setEnd(position.node, position.offset + 1);
  const rects = range.getClientRects();
  if (rects.length === 0) return null;
  const rect = rects[0];
  return rect.height > 0 ? rect.top + rect.height / 2 : null;
}

// A newline or a collapsed space has no box of its own, so the nearest character that has one answers for it.
function lineCentre(doc: Document, nodes: Text[], starts: number[], total: number, index: number): number | null {
  for (let step = 0; step < EMPTY_PROBE_REACH; step++) {
    if (index + step < total) {
      const ahead = centreOf(doc, positionAt(nodes, starts, index + step));
      if (ahead !== null) return ahead;
    }
    if (index - step >= 0) {
      const behind = centreOf(doc, positionAt(nodes, starts, index - step));
      if (behind !== null) return behind;
    }
  }
  return null;
}

function hideTextFrom(doc: Document, nodes: Text[], start: TextPosition): void {
  const from = nodes.indexOf(start.node);
  for (let index = from; index < nodes.length; index++) {
    let target = nodes[index];
    if (index === from && start.offset > 0) target = target.splitText(start.offset);
    const wrapper = doc.createElement('span');
    wrapper.className = OVERFLOW_CLASS;
    target.parentNode!.insertBefore(wrapper, target);
    wrapper.appendChild(target);
  }
}

// line-clamp only draws the ellipsis: the lines after it are still painted, and at a line height of
// one their tops reach into the ink room under the last line. Only their paint goes, never their layout.
export function hideClampOverflow(element: HTMLElement, maxLines: number): void {
  revealClampOverflow(element);
  if (maxLines <= 1) return;

  const view = element.ownerDocument.defaultView;
  if (view === null) return;
  const box = element.getBoundingClientRect();
  if (element.offsetHeight <= 0 || element.offsetWidth <= 0) return;
  const scale = box.height / element.offsetHeight;
  // A rotated or skewed ancestor makes client rects axis-aligned hulls, which no longer say which line a glyph is on.
  if (!(scale > 0) || Math.abs(box.width / element.offsetWidth - scale) > SCALE_TOLERANCE * scale) return;

  const style = view.getComputedStyle(element);
  const pitch = parseFloat(style.lineHeight);
  const paddingTop = parseFloat(style.paddingTop);
  const borderTop = parseFloat(style.borderTopWidth) || 0;
  if (!Number.isFinite(pitch) || !Number.isFinite(paddingTop)) return;
  const boundary = box.top + (borderTop + paddingTop + maxLines * pitch) * scale;

  const doc = element.ownerDocument;
  const nodes = textNodes(element);
  const starts: number[] = [];
  let total = 0;
  for (const node of nodes) {
    starts.push(total);
    total += node.length;
  }

  if (total > 0) {
    const last = lineCentre(doc, nodes, starts, total, total - 1);
    if (last !== null && last >= boundary) {
      let low = 0;
      let high = total - 1;
      while (low < high) {
        const middle = (low + high) >> 1;
        const centre = lineCentre(doc, nodes, starts, total, middle);
        if (centre !== null && centre >= boundary) high = middle;
        else low = middle + 1;
      }
      hideTextFrom(doc, nodes, positionAt(nodes, starts, low));
    }
  }

  const images = element.querySelectorAll('img');
  for (let index = 0; index < images.length; index++) {
    const rect = images[index].getBoundingClientRect();
    if (rect.height > 0 && rect.top + rect.height / 2 >= boundary) images[index].classList.add(OVERFLOW_CLASS);
  }
}
