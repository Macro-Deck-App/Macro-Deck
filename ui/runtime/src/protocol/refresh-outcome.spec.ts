import { isUnreachable } from './refresh-outcome';
import { TransportError } from './transport-error';

describe('isUnreachable', () => {
  it('treats a non-transport failure as unreachable', () => {
    expect(isUnreachable(new TypeError('Failed to fetch'))).toBeTrue();
    expect(isUnreachable(new DOMException('The operation was aborted.', 'AbortError'))).toBeTrue();
    expect(isUnreachable(new SyntaxError('Unexpected token <'))).toBeTrue();
  });

  it('treats a 401 or 403 as the host refusing the session, not being unreachable', () => {
    expect(isUnreachable(new TransportError(401, 'Unauthorized'))).toBeFalse();
    expect(isUnreachable(new TransportError(403, 'Forbidden'))).toBeFalse();
  });

  [500, 503, 408, 429, 400, 404, 0].forEach(status => {
    it(`treats a ${status} response as unreachable, not a refusal`, () => {
      expect(isUnreachable(new TransportError(status, 'Problem'))).toBeTrue();
    });
  });
});
