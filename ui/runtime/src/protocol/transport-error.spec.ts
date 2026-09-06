import { TransportError } from './transport-error';

describe('TransportError', () => {
  it('is catchable by type', () => {
    // The reason this is worth a test: callers branch on `instanceof` in a catch block, and the ES5
    // target this package ships breaks exactly that for a subclass of a built-in. `Error.call(this)`
    // returns a fresh object, so without an explicit prototype restore the thrown value is a plain
    // Error and every such branch silently takes the wrong path.
    let caught: unknown;
    try {
      throw new TransportError(404, 'Not found', 'not_found');
    } catch (error) {
      caught = error;
    }

    expect(caught instanceof TransportError).toBeTrue();
    expect(caught instanceof Error).toBeTrue();
  });

  it('carries the status, message and code it was given', () => {
    const error = new TransportError(503, 'Service unavailable', 'unavailable');

    expect(error.status).toBe(503);
    expect(error.message).toBe('Service unavailable');
    expect(error.code).toBe('unavailable');
    expect(error.name).toBe('TransportError');
  });

  it('leaves the code undefined when the host sent none', () => {
    expect(new TransportError(500, 'Server error').code).toBeUndefined();
  });
});
