import { TransportError } from './transport-error';

export type RefreshOutcome = 'ok' | 'invalid' | 'unreachable';

// Only a TransportError with 401/403 is a refusal; everything else counts as unreachable. Erring
// toward unreachable costs a retry, erring the other way signs out a session that is still good.
// A 5xx never means a spent cookie - a host with a locked key ring answers 503 to everything.
export function isUnreachable(error: unknown): boolean {
  if (error instanceof TransportError) {
    return error.status !== 401 && error.status !== 403;
  }
  return true;
}
