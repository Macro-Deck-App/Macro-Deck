import { isUnreachable } from '../protocol/refresh-outcome';
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

  describe('with a request timeout', () => {
    beforeEach(() => jasmine.clock().install());
    afterEach(() => jasmine.clock().uninstall());

    function hangingFetch(): Array<{ url: string; init: RequestInit }> {
      const calls: Array<{ url: string; init: RequestInit }> = [];
      (globalThis as { fetch: unknown }).fetch = (url: string, init: RequestInit) => {
        calls.push({ url, init });
        return new Promise<Response>((_resolve, reject) => {
          init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
        });
      };
      return calls;
    }

    it('gives up on a request the host never answers, as an unreachable host', async () => {
      hangingFetch();
      let failure: unknown = null;

      const request = client({ requestTimeoutMs: () => 15_000 }).get('/api/x').catch(error => { failure = error; });
      jasmine.clock().tick(14_999);
      await Promise.resolve();
      expect(failure).toBeNull();

      jasmine.clock().tick(1);
      await request;

      expect(failure).not.toBeNull();
      expect(isUnreachable(failure)).toBeTrue();
    });

    function slowBody(arrivesAfterMs: number | null): void {
      (globalThis as { fetch: unknown }).fetch = (_url: string, init: RequestInit) => Promise.resolve({
        ok: true,
        status: 200,
        statusText: '',
        text: () => new Promise<string>((resolve, reject) => {
          init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
          if (arrivesAfterMs !== null) setTimeout(() => resolve('{"name":"Deck"}'), arrivesAfterMs);
        }),
      } as unknown as Response);
    }

    it('lets a body that streams past the header timeout finish', async () => {
      slowBody(45_000);

      const request = client({ requestTimeoutMs: () => 15_000 }).get<{ name: string }>('/api/x');
      for (let turn = 0; turn < 5; turn++) await Promise.resolve();
      jasmine.clock().tick(45_000);

      await expectAsync(request).toBeResolvedTo({ name: 'Deck' });
    });

    it('gives up on a body that stops arriving, as an unreachable host', async () => {
      slowBody(null);
      let failure: unknown = null;

      const request = client({ requestTimeoutMs: () => 15_000 }).get('/api/x').catch(error => { failure = error; });
      for (let turn = 0; turn < 5; turn++) await Promise.resolve();
      jasmine.clock().tick(10 * 60_000);
      await request;

      expect(isUnreachable(failure)).toBeTrue();
    });

    it('times out the retry after a refreshed session on its own', async () => {
      let call = 0;
      (globalThis as { fetch: unknown }).fetch = (_url: string, init: RequestInit) => {
        call++;
        if (call === 1) {
          return Promise.resolve({ ok: false, status: 401, statusText: '', text: () => Promise.resolve('') } as unknown as Response);
        }
        return new Promise<Response>((_resolve, reject) => {
          init.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')));
        });
      };

      const request = client({ requestTimeoutMs: () => 15_000, onUnauthorized: () => Promise.resolve(true) })
        .get('/api/x');
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
      expect(call).toBe(2);
      jasmine.clock().tick(15_000);

      await expectAsync(request).toBeRejected();
    });

    it('still aborts when the caller cancels before the timeout', async () => {
      hangingFetch();
      const caller = new AbortController();

      const request = client({ requestTimeoutMs: () => 15_000 }).get('/api/x', caller.signal);
      caller.abort();

      await expectAsync(request).toBeRejected();
    });

    it('waits as long as the host takes when no timeout is set', async () => {
      const calls = hangingFetch();
      let settled = false;

      void client().get('/api/x').finally(() => { settled = true; });
      jasmine.clock().tick(10 * 60_000);
      await Promise.resolve();

      expect(settled).toBeFalse();
      expect(calls[0].init.signal).toBeUndefined();
    });
  });
});
