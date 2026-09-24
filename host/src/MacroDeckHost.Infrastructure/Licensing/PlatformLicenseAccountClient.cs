using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Store.Reviews;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Licensing;

public sealed partial class PlatformLicenseAccountClient : IPlatformLicenseAccountClient, IDisposable
{
	public const string HttpClientName = "platform-license-account";
	public const int MaximumResponseBytes = 64 * 1024;

	private const string AccountPath = "api/v1/companion-licenses/account";
	private const string AccountLicenseExists = "account-license-exists";

	private static readonly TimeSpan RequestTimeoutMargin = TimeSpan.FromSeconds(20);
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly Uri _baseUrl;
	private readonly IConnectSessionService _session;
	private readonly ILogger _logger;

	public PlatformLicenseAccountClient(IHttpClientFactory httpClientFactory,
		StorePlatformOptions options,
		IConnectSessionService session,
		ILogger logger)
		: this(httpClientFactory.CreateClient(HttpClientName), false, options, session, logger)
	{
	}

	internal PlatformLicenseAccountClient(HttpMessageHandler handler,
		StorePlatformOptions options,
		IConnectSessionService session,
		ILogger logger)
		: this(new HttpClient(handler, disposeHandler: false), true, options, session, logger)
	{
	}

	private PlatformLicenseAccountClient(HttpClient http,
		bool ownsClient,
		StorePlatformOptions options,
		IConnectSessionService session,
		ILogger logger)
	{
		_http = http;
		_ownsClient = ownsClient;
		_baseUrl = options.BaseUrl;
		_session = session;
		_logger = logger.ForContext<PlatformLicenseAccountClient>();
	}

	public Task<PlatformAccountLicenseResult> GetAccountLicenseAsync(long? afterRevision,
		TimeSpan wait,
		CancellationToken cancellationToken)
	{
		var path = afterRevision is { } revision
			? string.Create(CultureInfo.InvariantCulture,
				$"{AccountPath}?after={revision}&wait={(int)Math.Max(0, wait.TotalSeconds)}")
			: AccountPath;
		return SendAsync(HttpMethod.Get, path, null, afterRevision is null ? TimeSpan.Zero : wait, cancellationToken);
	}

	public Task<PlatformAccountLicenseResult> StoreAccountLicenseAsync(string license,
		CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Put,
			AccountPath,
			JsonContent.Create(new { license }, options: Json),
			TimeSpan.Zero,
			cancellationToken);

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private async Task<PlatformAccountLicenseResult> SendAsync(HttpMethod method,
		string path,
		HttpContent? content,
		TimeSpan wait,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, new Uri(_baseUrl, path)) { Content = content };
		if (_session.Current.Status != ConnectAccountStatus.SignedIn)
		{
			return new PlatformAccountLicenseResult.SignedOut();
		}

		try
		{
			request.Headers.Authorization =
				new AuthenticationHeaderValue("Bearer", await _session.GetAccessToken(cancellationToken));
		}
		catch (Exception ex) when (ex is ConnectAuthRejectedException or ConnectAccountSuspendedException)
		{
			return new PlatformAccountLicenseResult.SignedOut();
		}
		catch (ConnectAuthTransientException ex)
		{
			return new PlatformAccountLicenseResult.Unavailable(ex.RetryAfter);
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(wait + RequestTimeoutMargin);
		try
		{
			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			var result = Interpret(response, await ReadBodyAsync(response, timeout.Token));
			if (result is PlatformAccountLicenseResult.Unavailable)
			{
				_logger.Warning("The Macro Deck Platform answered {Status} to {Method} on the account Companion license",
					(int)response.StatusCode,
					method.Method);
			}

			return result;
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException ||
			ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
		{
			_logger.Warning("The account Companion license request {Method} failed: {Error}",
				method.Method,
				ex.GetType().Name);
			return new PlatformAccountLicenseResult.Unavailable(null);
		}
	}

	private static PlatformAccountLicenseResult Interpret(HttpResponseMessage response, JsonElement? body)
	{
		var retryAfter = RetryAfter(response);
		switch (response.StatusCode)
		{
			case HttpStatusCode.OK when ReadState(body) is { } current:
				return new PlatformAccountLicenseResult.Current(current.License, current.Revision, retryAfter);
			case HttpStatusCode.Conflict when Code(body) == AccountLicenseExists && ReadState(body) is { } conflict:
				return new PlatformAccountLicenseResult.Conflict(conflict.License, conflict.Revision);
			case HttpStatusCode.UnprocessableEntity:
				return new PlatformAccountLicenseResult.Refused(Code(body) ?? "invalid-license");
			case HttpStatusCode.Forbidden when Code(body) == "license-revoked":
				return new PlatformAccountLicenseResult.Refused("license-revoked");
			case HttpStatusCode.Forbidden:
				return new PlatformAccountLicenseResult.Unavailable(retryAfter, AccountBlocked: true);
			default:
				return new PlatformAccountLicenseResult.Unavailable(retryAfter);
		}
	}

	private static (string? License, long Revision)? ReadState(JsonElement? body)
	{
		if (body is not { ValueKind: JsonValueKind.Object } root ||
			!root.TryGetProperty("revision", out var revision) ||
			revision.ValueKind != JsonValueKind.Number ||
			!revision.TryGetInt64(out var value) ||
			value < 0)
		{
			return null;
		}

		if (!root.TryGetProperty("license", out var license) || license.ValueKind == JsonValueKind.Null)
		{
			return (null, value);
		}

		return license.ValueKind == JsonValueKind.String ? (license.GetString(), value) : null;
	}

	private static string? Code(JsonElement? body)
		=> body is { ValueKind: JsonValueKind.Object } root &&
			root.TryGetProperty("code", out var code) &&
			code.ValueKind == JsonValueKind.String &&
			code.GetString() is { } value &&
			ProblemCode().IsMatch(value)
				? value
				: null;

	private static async Task<JsonElement?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength > MaximumResponseBytes)
		{
			return null;
		}

		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		var buffer = new byte[MaximumResponseBytes + 1];
		var length = 0;
		int read;
		while (length < buffer.Length &&
			(read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken)) > 0)
		{
			length += read;
		}

		if (length == 0 || length > MaximumResponseBytes)
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
			return document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static TimeSpan? RetryAfter(HttpResponseMessage response)
	{
		if (response.Headers.RetryAfter is not { } header)
		{
			return null;
		}

		if (header.Delta is { } delta)
		{
			return delta;
		}

		return header.Date is { } date ? date - DateTimeOffset.UtcNow : null;
	}

	[GeneratedRegex("^[a-z][a-z-]{0,63}$")]
	private static partial Regex ProblemCode();
}
