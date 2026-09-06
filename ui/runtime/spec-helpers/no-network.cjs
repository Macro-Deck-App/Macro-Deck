// Node's fetch is undici, whose one-second heartbeat re-arms itself through the global setTimeout: armed
// while a spec holds jasmine's mock clock, a later tick runs it and throws. No spec wants a real request.
globalThis.fetch = (url) =>
  Promise.reject(new TypeError(`Failed to fetch: ${String(url)} was not stubbed and the specs make no real requests`));
