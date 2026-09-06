import { isSecureContext } from './secure-context';

function windowWith(overrides: { isSecureContext?: unknown; protocol: string; hostname: string }): never {
  return {
    isSecureContext: overrides.isSecureContext,
    location: { protocol: overrides.protocol, hostname: overrides.hostname },
  } as never;
}

describe('isSecureContext', () => {
  it('believes the browser when it has an answer', () => {
    expect(isSecureContext(windowWith({ isSecureContext: true, protocol: 'https:', hostname: 'deck.local' })))
      .toBeTrue();
    expect(isSecureContext(windowWith({ isSecureContext: false, protocol: 'https:', hostname: 'deck.local' })))
      .toBeFalse();
  });

  // Safari gained isSecureContext in 12.1; older engines report undefined, which a naive check reads
  // as "insecure" for a page that is plainly being served over HTTPS.
  it('falls back to the origin on a browser that does not implement it', () => {
    expect(isSecureContext(windowWith({ protocol: 'https:', hostname: 'deck.local' }))).toBeTrue();
    expect(isSecureContext(windowWith({ protocol: 'http:', hostname: 'deck.local' }))).toBeFalse();
  });

  it('treats loopback as trustworthy without https', () => {
    expect(isSecureContext(windowWith({ protocol: 'http:', hostname: 'localhost' }))).toBeTrue();
    expect(isSecureContext(windowWith({ protocol: 'http:', hostname: '127.0.0.1' }))).toBeTrue();
  });
});
