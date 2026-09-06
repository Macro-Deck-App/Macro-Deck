import { createHostUrlResolver, HostEndpointEnvironment } from './client-target';

describe('host url resolver', () => {
  let asked: string[];
  let hosts: string[];
  let environment: HostEndpointEnvironment;

  beforeEach(() => {
    asked = [];
    hosts = [];
    environment = {
      origin: 'http://page-origin',
      fetch: (input: string) => {
        asked.push(input);
        const ok = hosts.some(base => input.indexOf(base) === 0);
        return Promise.resolve({ ok } as Response);
      },
    };
  });

  it('dials the origin directly when the target asks for nothing else', async () => {
    const resolve = createHostUrlResolver({ kind: 'origin' }, environment);

    expect(await resolve()).toBe('http://page-origin');
    // The page came from there; asking whether it is a host would be a request for nothing.
    expect(asked.length).toBe(0);
  });

  it('takes the first candidate that answers as a host', async () => {
    hosts = ['http://127.0.0.1:8194'];
    const resolve = createHostUrlResolver({
      kind: 'probe',
      candidates: [{ baseUrl: 'http://127.0.0.1:8193' }, { baseUrl: 'http://127.0.0.1:8194' }],
    }, environment);

    expect(await resolve()).toBe('http://127.0.0.1:8194');
  });

  it('asks an endpoint only Macro Deck answers, so an unrelated open port is not mistaken for one', async () => {
    hosts = ['http://127.0.0.1:8193'];
    const resolve = createHostUrlResolver({
      kind: 'probe',
      candidates: [{ baseUrl: 'http://127.0.0.1:8193' }],
    }, environment);
    await resolve();

    expect(asked[0]).toBe('http://127.0.0.1:8193/api/system/build-info');
  });

  it('falls back to the origin the page was served from', async () => {
    hosts = ['http://page-origin'];
    const resolve = createHostUrlResolver({
      kind: 'probe',
      candidates: [{ baseUrl: 'http://127.0.0.1:8193' }],
    }, environment);

    // The one address known to work, which is what keeps a plain LAN visit working for a target
    // whose forwarded ports are absent.
    expect(await resolve()).toBe('http://page-origin');
  });

  it('answers null while nothing answers at all, rather than guessing', async () => {
    const resolve = createHostUrlResolver({
      kind: 'probe',
      candidates: [{ baseUrl: 'http://127.0.0.1:8193' }],
    }, environment);

    // A device that booted before the host did; the caller retries rather than failing outright.
    expect(await resolve()).toBeNull();
  });

  it('stops asking once a candidate has answered', async () => {
    hosts = ['http://127.0.0.1:8193'];
    const resolve = createHostUrlResolver({
      kind: 'probe',
      candidates: [{ baseUrl: 'http://127.0.0.1:8193' }, { baseUrl: 'http://127.0.0.1:8194' }],
    }, environment);
    await resolve();

    expect(asked.length).toBe(1);
  });
});
