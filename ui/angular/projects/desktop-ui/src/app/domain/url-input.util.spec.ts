import { isValidUrl, normalizeHttpsUrl } from './url-input.util';

describe('URL input helpers', () => {
  it('adds https to a URL with no protocol', () => {
    expect(normalizeHttpsUrl('macro-deck.app/docs')).toBe('https://macro-deck.app/docs');
    expect(normalizeHttpsUrl('localhost:8193')).toBe('https://localhost:8193');
  });

  it('keeps an explicit protocol and template expression unchanged', () => {
    expect(normalizeHttpsUrl('http://localhost:8193')).toBe('http://localhost:8193');
    expect(normalizeHttpsUrl('{{ vars.website }}')).toBe('{{ vars.website }}');
  });

  it('turns a protocol-relative URL into an HTTPS URL', () => {
    expect(normalizeHttpsUrl('//macro-deck.app')).toBe('https://macro-deck.app');
  });

  it('rejects HTTP(S) URLs with only one slash after the protocol', () => {
    expect(isValidUrl('https:/macro-deck.app')).toBeFalse();
    expect(isValidUrl('http:/localhost:8193')).toBeFalse();
  });

  it('accepts correctly formed HTTP(S) URLs', () => {
    expect(isValidUrl('https://macro-deck.app/docs')).toBeTrue();
    expect(isValidUrl('http://localhost:8193')).toBeTrue();
  });
});
