import { TransportError } from '../protocol/transport-error';
import { HttpClient, HttpClientOptions } from './http-client';

interface Call {
  url: string;
  init: RequestInit;
}

function stubFetch(responses: Array<{ status: number; body?: string; statusText?: string }>): Call[] {
  const calls: Call[] = [];
  let index = 0;
  (globalThis as { fetch: unknown }).fetch = (url: string, init: RequestInit) => {
    calls.push({ url, init });
    const answer = responses[Math.min(index++, responses.length - 1)];
    return Promise.resolve({
      ok: answer.status >= 200 && answer.status < 300,
      status: answer.status,
      statusText: answer.statusText ?? '',
      text: () => Promise.resolve(answer.body ?? ''),
    } as unknown as Response);
  };
  return calls;
}

describe('HttpClient', () => {
  const original = (globalThis as { fetch?: unknown }).fetch;
  afterEach(() => { (globalThis as { fetch?: unknown }).fetch = original; });

  const client = (overrides: Partial<HttpClientOptions> = {}) =>
    new HttpClient({ baseUrl: () => 'http://host', ...overrides });

  it('parses a JSON body', async () => {
    stubFetch([{ status: 200, body: '{"name":"Deck"}' }]);

    await expectAsync(client().get<{ name: string }>('/api/x')).toBeResolvedTo({ name: 'Deck' });
  });

  it('treats an empty body as done rather than as malformed JSON', async () => {
    stubFetch([{ status: 204 }]);

    await expectAsync(client().delete('/api/x')).toBeResolvedTo(undefined as never);
  });

  it('sends the bearer the client holds, and none when it holds none', async () => {
    const withToken = stubFetch([{ status: 200 }]);
    await client({ accessToken: () => 'abc' }).get('/api/x');
    expect((withToken[0].init.headers as Record<string, string>)['Authorization']).toBe('Bearer abc');

    const without = stubFetch([{ status: 200 }]);
    await client().get('/api/x');
    expect((without[0].init.headers as Record<string, string>)['Authorization']).toBeUndefined();
  });

  it('never lets a cached answer stand in for the deck state', async () => {
    const calls = stubFetch([{ status: 200 }]);

    await client().get('/api/x');

    expect(calls[0].init.cache).toBe('no-store');
  });

  it('raises a typed error carrying the host problem detail', async () => {
    stubFetch([{ status: 409, body: '{"title":"Folder is not empty","code":"folder_not_empty"}' }]);

    await client().get('/api/x').then(
      () => fail('expected a rejection'),
      (error: unknown) => {
        expect(error instanceof TransportError).toBeTrue();
        expect((error as TransportError).status).toBe(409);
        expect((error as TransportError).message).toBe('Folder is not empty');
        expect((error as TransportError).code).toBe('folder_not_empty');
      });
  });

  it('falls back to the status text when the failure carried no body', async () => {
    stubFetch([{ status: 500, statusText: 'Server Error' }]);

    await client().get('/api/x').then(
      () => fail('expected a rejection'),
      (error: unknown) => expect((error as TransportError).message).toBe('Server Error'));
  });

  it('retries a 401 once when the credential can be renewed', async () => {
    const calls = stubFetch([{ status: 401 }, { status: 200, body: '{"ok":true}' }]);

    const result = await client({ onUnauthorized: () => Promise.resolve(true) })
      .get<{ ok: boolean }>('/api/x');

    expect(calls.length).toBe(2);
    expect(result).toEqual({ ok: true });
  });

  it('does not retry a 401 when nobody can renew the credential', async () => {
    const calls = stubFetch([{ status: 401 }]);

    await client({ onUnauthorized: () => Promise.resolve(false) }).get('/api/x')
      .then(() => fail('expected a rejection'), () => undefined);

    expect(calls.length).toBe(1);
  });

  it('never retries a 403, because a refresh cannot widen a scope', async () => {
    const calls = stubFetch([{ status: 403 }]);
    let told = false;

    await client({ onForbidden: () => { told = true; }, onUnauthorized: () => Promise.resolve(true) })
      .get('/api/x').then(() => fail('expected a rejection'), () => undefined);

    expect(calls.length).toBe(1);
    expect(told).toBeTrue();
  });

  it('sends a body as JSON and says so', async () => {
    const calls = stubFetch([{ status: 200 }]);

    await client().post('/api/x', { name: 'Deck' });

    expect(calls[0].init.body).toBe('{"name":"Deck"}');
    expect((calls[0].init.headers as Record<string, string>)['Content-Type']).toBe('application/json');
  });

  it('resolves the host address per request rather than once', async () => {
    let host = 'http://first';
    const calls = stubFetch([{ status: 200 }]);
    const moving = new HttpClient({ baseUrl: () => host });

    await moving.get('/api/x');
    host = 'http://second';
    await moving.get('/api/x');

    expect(calls.map(call => call.url))
      .toEqual(['http://first/api/x', 'http://second/api/x']);
  });
});
