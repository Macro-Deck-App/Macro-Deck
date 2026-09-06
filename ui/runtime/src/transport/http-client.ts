import { TransportError } from '../protocol/transport-error';

export interface HttpClientOptions {
  baseUrl(): string;

  accessToken?(): string | undefined;

  onUnauthorized?(): Promise<boolean>;

  onForbidden?(): void;
}

export class HttpClient {
  constructor(private readonly options: HttpClientOptions) {}

  get<T>(path: string, signal?: AbortSignal): Promise<T> {
    return this.send<T>('GET', path, undefined, signal);
  }

  post<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    return this.send<T>('POST', path, body, signal);
  }

  put<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    return this.send<T>('PUT', path, body, signal);
  }

  patch<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    return this.send<T>('PATCH', path, body, signal);
  }

  delete<T>(path: string, signal?: AbortSignal): Promise<T> {
    return this.send<T>('DELETE', path, undefined, signal);
  }

  async send<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
    const response = await this.fetchWithAuth(method, path, body, signal);
    return HttpClient.parse<T>(response);
  }

  async fetchWithAuth(
    method: string,
    path: string,
    body?: unknown,
    signal?: AbortSignal,
    extraHeaders?: Record<string, string>,
  ): Promise<Response> {
    const response = await fetch(
      this.options.baseUrl() + path, this.buildInit(method, body, signal, extraHeaders));

    if (response.status === 403) {
      if (this.options.onForbidden) this.options.onForbidden();
      return response;
    }

    if (response.status !== 401 || !this.options.onUnauthorized) return response;

    const retry = await this.options.onUnauthorized();
    if (!retry) return response;

    return fetch(this.options.baseUrl() + path, this.buildInit(method, body, signal, extraHeaders));
  }

  private buildInit(
    method: string,
    body: unknown,
    signal?: AbortSignal,
    extraHeaders?: Record<string, string>,
  ): RequestInit {
    const headers: Record<string, string> = {};
    for (const name in extraHeaders) {
      if (Object.prototype.hasOwnProperty.call(extraHeaders, name)) headers[name] = extraHeaders[name];
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';

    const token = this.options.accessToken ? this.options.accessToken() : undefined;
    if (token) headers['Authorization'] = `Bearer ${token}`;

    const init: RequestInit = { method, headers, cache: 'no-store' };
    if (body !== undefined) init.body = JSON.stringify(body);
    if (signal) init.signal = signal;
    return init;
  }

  private static async parse<T>(response: Response): Promise<T> {
    const text = await response.text();

    if (!response.ok) {
      const problem = HttpClient.tryParseJson(text);
      const message = problem?.title ?? problem?.detail ?? problem?.message
        ?? response.statusText ?? `HTTP ${response.status}`;
      throw new TransportError(response.status, message, problem?.code);
    }

    // 204 and an empty 200 both mean "done", and neither is JSON.
    return (text.length === 0 ? undefined : JSON.parse(text)) as T;
  }

  private static tryParseJson(text: string): {
    title?: string;
    detail?: string;
    message?: string;
    code?: string;
  } | undefined {
    if (text.length === 0) return undefined;
    try {
      const parsed = JSON.parse(text) as unknown;
      return typeof parsed === 'object' && parsed !== null
        ? parsed as { title?: string; detail?: string; message?: string; code?: string }
        : undefined;
    } catch {
      return undefined;
    }
  }
}
