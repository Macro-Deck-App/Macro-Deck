// new URL(...) is the fast path but does not exist below Chrome 32, and this package's
// compatibility floor is Chrome 30 - hence the string fallback doing the same job.
export function websocketUrl(baseUrl: string, documentUrl?: string): string {
  const resolved = resolveAgainstDocument(baseUrl, documentUrl);
  return resolved.replace(/^http:/i, 'ws:').replace(/^https:/i, 'wss:').replace(/\/$/, '');
}

function resolveAgainstDocument(baseUrl: string, documentUrl?: string): string {
  const base = documentUrl
    ?? (typeof location === 'undefined' ? undefined : location.href);

  if (typeof URL === 'function') {
    try {
      return new URL(baseUrl, base).toString();
    } catch {
      // A base the URL parser rejects is still worth trying to resolve by hand below.
    }
  }

  if (/^[a-z][a-z0-9+.-]*:/i.test(baseUrl)) return baseUrl;
  if (base === undefined) return baseUrl;

  const origin = base.replace(/^([a-z][a-z0-9+.-]*:\/\/[^/]*).*$/i, '$1');
  if (baseUrl.indexOf('//') === 0) return base.replace(/^([a-z][a-z0-9+.-]*:).*$/i, '$1') + baseUrl;
  if (baseUrl.charAt(0) === '/') return origin + baseUrl;

  const directory = base.replace(/[?#].*$/, '').replace(/[^/]*$/, '');
  return directory + baseUrl;
}
