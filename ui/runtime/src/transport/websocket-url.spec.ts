import { websocketUrl } from './websocket-url';

describe('websocket url', () => {
  const platformUrl = (globalThis as { URL?: unknown }).URL;

  const cases = (label: string) => {
    describe(label, () => {
      const DOC = 'https://deck.example/admin/index.html';

      it('turns http into ws and https into wss', () => {
        expect(websocketUrl('http://host:8194', DOC)).toBe('ws://host:8194');
        expect(websocketUrl('https://host:8194', DOC)).toBe('wss://host:8194');
      });

      it('drops the trailing slash the caller would double', () => {
        expect(websocketUrl('http://host/', DOC)).toBe('ws://host');
      });

      it('resolves an absolute path against the document origin', () => {
        expect(websocketUrl('/api', DOC)).toBe('wss://deck.example/api');
      });

      it('resolves a protocol-relative address against the document scheme', () => {
        expect(websocketUrl('//other.example', DOC)).toBe('wss://other.example');
      });

      it('resolves a relative address against the document directory', () => {
        expect(websocketUrl('sub', DOC)).toBe('wss://deck.example/admin/sub');
      });

      it('leaves an address that already names its own scheme alone', () => {
        expect(websocketUrl('https://elsewhere.example/x', DOC)).toBe('wss://elsewhere.example/x');
      });
    });
  };

  cases('with the platform URL');

  describe('without the platform URL', () => {
    beforeAll(() => { delete (globalThis as { URL?: unknown }).URL; });
    afterAll(() => { (globalThis as { URL?: unknown }).URL = platformUrl; });

    cases('fallback');
  });
});
