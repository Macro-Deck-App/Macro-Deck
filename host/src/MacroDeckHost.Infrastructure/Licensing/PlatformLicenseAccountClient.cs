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
	private const string PromoCodePath = "api/v1/companion-licenses/promo-code";
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

	public async Task<PlatformPromoCodeResult> RedeemPromoCodeAsync(string code, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, PromoCodePath))
		{
			Content = JsonContent.Create(new { code }, options: Json)
		};
		var authorization = await AuthorizeAsync(request, cancellationToken);
		switch (authorization.Kind)
		{
			case AuthorizationKind.SignedOut:
				return new PlatformPromoCodeResult.SignedOut();
			case AuthorizationKind.Suspended:
				return new PlatformPromoCodeResult.AccountSuspended();
			case AuthorizationKind.Transient:
				return new PlatformPromoCodeResult.Unavailable();
		}

		return await ExecuteAsync(request,
			TimeSpan.Zero,
			(response, body) =>
			{
				var result = InterpretPromoCode(response, body);
				if (result is PlatformPromoCodeResult.Unavailable)
				{
					_logger.Warning("The Macro Deck Platform answered {Status} to a promo code redemption",
						(int)response.StatusCode);
				}

				return result;
			},
			new PlatformPromoCodeResult.Unavailable(),
			cancellationToken);
	}

	private async Task<PlatformAccountLicenseResult> SendAsync(HttpMethod method,
		string path,
		HttpContent? content,
		TimeSpan wait,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, new Uri(_baseUrl, path)) { Content = content };
		var authorization = await AuthorizeAsync(request, cancellationToken);
		switch (authorization.Kind)
		{
			case AuthorizationKind.SignedOut:
			case AuthorizationKind.Suspended:
				return new PlatformAccountLicenseResult.SignedOut();
			case AuthorizationKind.Transient:
				return new PlatformAccountLicenseResult.Unavailable(authorization.RetryAfter);
		}

		return await ExecuteAsync(request,
			wait,
			(response, body) =>
			{
				var result = Interpret(response, body);
				if (result is PlatformAccountLicenseResult.Unavailable)
				{
					_logger.Warning("The Macro Deck Platform answered {Status} to {Method} on the account Companion license",
						(int)response.StatusCode,
						method.Method);
				}

				return result;
			},
			new PlatformAccountLicenseResult.Unavailable(null),
			cancellationToken);
	}

	private async Task<Authorization> AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (_session.Current.Status != ConnectAccountStatus.SignedIn)
		{
			return new Authorization(AuthorizationKind.SignedOut, null);
		}

		try
		{
			request.Headers.Authorization =
				new AuthenticationHeaderValue("Bearer", await _session.GetAccessToken(cancellationToken));
			return new Authorization(AuthorizationKind.Authorized, null);
		}
		catch (ConnectAuthRejectedException)
		{
			return new Authorization(AuthorizationKind.SignedOut, null);
		}
		catch (ConnectAccountSuspendedException)
		{
			return new Authorization(AuthorizationKind.Suspended, null);
		}
		catch (ConnectAuthTransientException ex)
		{
			return new Authorization(AuthorizationKind.Transient, ex.RetryAfter);
		}
	}

	private async Task<T> ExecuteAsync<T>(HttpRequestMessage request,
		TimeSpan wait,
		Func<HttpResponseMessage, JsonElement?, T> interpret,
		T failed,
		CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(wait + RequestTimeoutMargin);
		try
		{
			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			return interpret(response, await ReadBodyAsync(response, timeout.Token));
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or NotSupportedException ||
			ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
		{
			_logger.Warning("The Companion license request {Method} failed: {Error}",
				request.Method.Method,
				ex.GetType().Name);
			return failed;
		}
	}

	private static PlatformPromoCodeResult InterpretPromoCode(HttpResponseMessage response, JsonElement? body)
	{
		var code = Code(body);
		switch (response.StatusCode)
		{
			case HttpStatusCode.OK when ReadState(body) is { License: { Length: > 0 } license }:
				return new PlatformPromoCodeResult.Redeemed(license);
			case HttpStatusCode.Conflict when code == AccountLicenseExists && ReadState(body) is { License: { Length: > 0 } existing }:
				return new PlatformPromoCodeResult.AccountLicenseExists(existing);
			case HttpStatusCode.Conflict when code == "promo-code-redeemed":
				return new PlatformPromoCodeResult.Rejected(PromoCodeRejection.AlreadyRedeemed);
			case HttpStatusCode.NotFound when code == "invalid-promo-code":
			case HttpStatusCode.BadRequest:
				return new PlatformPromoCodeResult.Rejected(PromoCodeRejection.Invalid);
			case HttpStatusCode.Gone when code == "promo-code-expired":
				return new PlatformPromoCodeResult.Rejected(PromoCodeRejection.Expired);
			case HttpStatusCode.Forbidden when code == "license-revoked":
				return new PlatformPromoCodeResult.Rejected(PromoCodeRejection.Revoked);
			case HttpStatusCode.Forbidden:
				return new PlatformPromoCodeResult.AccountSuspended();
			case HttpStatusCode.Unauthorized:
				return new PlatformPromoCodeResult.SignedOut();
			case HttpStatusCode.TooManyRequests:
				return new PlatformPromoCodeResult.RateLimited(RetryAfter(response));
			default:
				return new PlatformPromoCodeResult.Unavailable();
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

	private enum AuthorizationKind
	{
		Authorized,
		SignedOut,
		Suspended,
		Transient
	}

	private readonly record struct Authorization(AuthorizationKind Kind, TimeSpan? RetryAfter);

	[GeneratedRegex("^[a-z][a-z-]{0,63}$")]
	private static partial Regex ProblemCode();
}
