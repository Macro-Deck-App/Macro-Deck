export function element<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
): HTMLElementTagNameMap[K] {
  const created = document.createElement(tag);
  if (className !== undefined) created.className = className;
  return created;
}

export function setClass(target: Element, name: string, on: boolean): void {
  // `classList.toggle(name, force)` only grew its second argument in Safari 10.
  if (on) target.classList.add(name);
  else target.classList.remove(name);
}

export function setAttribute(target: Element, name: string, value: string | null): void {
  if (value === null) target.removeAttribute(name);
  else target.setAttribute(name, value);
}

const SVG_NS = 'http://www.w3.org/2000/svg';

export function strokeIcon(className: string, paths: string[]): SVGElement {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('class', className);
  svg.setAttribute('viewBox', '0 0 24 24');
  // An intrinsic size as well as the viewBox: an inline <svg> sized only by CSS falls back to the
  // replaced-element default of 300x150 on the compatibility floor, which is how a 44px button
  // ended up with an icon spilling out of it (issue #829). CSS still wins where CSS is honoured.
  svg.setAttribute('width', '24');
  svg.setAttribute('height', '24');
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  svg.setAttribute('aria-hidden', 'true');
  for (let index = 0; index < paths.length; index++) {
    const path = document.createElementNS(SVG_NS, 'path');
    path.setAttribute('d', paths[index]);
    svg.appendChild(path);
  }
  return svg;
}

export function dismissButton(className: string, iconClassName: string, label: string): HTMLButtonElement {
  const button = element('button', className);
  button.type = 'button';
  button.setAttribute('aria-label', label);
  button.appendChild(strokeIcon(iconClassName, ['M18 6 6 18', 'm6 6 12 12']));
  return button;
}
