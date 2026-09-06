import { restorePrototype } from '../util/restore-prototype';

export class TransportError extends Error {
  readonly status: number;
  readonly code?: string;

  constructor(status: number, message: string, code?: string) {
    super(message);
    restorePrototype(this, TransportError.prototype);
    this.name = 'TransportError';
    this.status = status;
    this.code = code;
  }
}
