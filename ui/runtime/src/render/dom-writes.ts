interface StyleCache {
  [property: string]: string;
}

// The compatibility floor only knows these behind the prefix (PREFIXED_AT_FLOOR in the web client)
// and silently drops the standard name, so both are always written and cleared together.
const PREFIXED_AT_FLOOR: { [property: string]: string } = {
  'transform': '-webkit-transform',
  'transform-origin': '-webkit-transform-origin',
  'filter': '-webkit-filter',
};

function styleCacheOf(element: HTMLElement | SVGElement): StyleCache {
  const withCache = element as unknown as { __mdStyleCache?: StyleCache };
  if (withCache.__mdStyleCache === undefined) withCache.__mdStyleCache = {};
  return withCache.__mdStyleCache;
}

export function setStyle(element: HTMLElement | SVGElement, name: string, value: string | null): boolean {
  const normalized = value === null ? '' : value;
  const cache = styleCacheOf(element);
  if (cache[name] === normalized) return false;
  cache[name] = normalized;
  // `setProperty(name, null)` is the documented removal, but the oldest engines in the baseline
  // ignore it, so an empty string does the removing instead.
  const style = (element as HTMLElement).style;
  const prefixed = PREFIXED_AT_FLOOR[name];
  if (prefixed !== undefined) style.setProperty(prefixed, normalized);
  style.setProperty(name, normalized);
  return true;
}

export function setClass(element: Element, name: string, on: boolean): boolean {
  if (element.classList.contains(name) === on) return false;
  // `classList.toggle(name, force)` only grew its second argument in Safari 10.
  if (on) element.classList.add(name);
  else element.classList.remove(name);
  return true;
}

export function setClassName(element: Element, value: string): boolean {
  if (element.getAttribute('class') === value) return false;
  element.setAttribute('class', value);
  return true;
}

export function setAttribute(element: Element, name: string, value: string | null): boolean {
  if (value === null) {
    if (element.getAttribute(name) === null) return false;
    element.removeAttribute(name);
    return true;
  }
  if (element.getAttribute(name) === value) return false;
  element.setAttribute(name, value);
  return true;
}
