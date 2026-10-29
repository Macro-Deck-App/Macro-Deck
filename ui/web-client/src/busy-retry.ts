export const TOO_MANY_SESSIONS = 'TOO_MANY_SESSIONS';
export const MAX_BUSY_RETRIES = 6;

export function busyRetryDelayMs(attempt: number): number {
  return Math.min(1000 * 2 ** attempt, 16000);
}
