import type { ActiveNotification } from '@macro-deck/runtime';

let serial = 0;

export function notification(overrides: Partial<ActiveNotification> = {}): ActiveNotification {
  serial++;
  return {
    id: 'n' + serial,
    revision: 1,
    sourceCategory: 'plugin',
    sourceId: 'acme.plugin',
    sourceName: 'Acme',
    title: 'Title ' + serial,
    severity: 'info',
    popup: 'toast',
    inbox: false,
    dismissible: true,
    createdAt: new Date(1000 * serial).toISOString(),
    actions: [],
    ...overrides,
  };
}

export interface RecordedRequest {
  type: string;
  payload: unknown;
}

export function recordingRequest(
  answer: (request: RecordedRequest) => unknown = () => ({ status: 'accepted' }),
): { calls: RecordedRequest[]; request(type: string, payload: unknown): Promise<unknown> } {
  const calls: RecordedRequest[] = [];
  return {
    calls,
    request: (type, payload) => {
      const call = { type, payload };
      calls.push(call);
      return Promise.resolve(answer(call));
    },
  };
}

export const settle = async (): Promise<void> => {
  for (let turn = 0; turn < 8; turn++) await Promise.resolve();
};
