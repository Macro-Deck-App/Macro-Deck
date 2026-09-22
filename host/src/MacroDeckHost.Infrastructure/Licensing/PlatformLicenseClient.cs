using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Licensing;

public sealed partial class PlatformLicenseClient : IPlatformLicenseClient, IDisposable
{
	public const string HttpClientName = "platform-licensing";
	public const int MaximumRevokedLicenseIds = 10_000;

	private const string IssuePath = "api/v1/companion-licenses";
	private const string RevocationsPath = "api/v1/companion-licenses/revocations";

	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private static readonly HashSet<string> RetryableRefusals =
		new(StringComparer.Ordinal) { "purchase-not-found", "purchase-not-completed" };

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly Uri _baseUrl;
	private readonly ILogger _logger;

	public PlatformLicenseClient(IHttpClientFactory httpClientFactory, StorePlatformOptions options, ILogger logger)
		: this(httpClientFactory.CreateClient(HttpClientName), false, options, logger)
	{
	}

	internal PlatformLicenseClient(HttpMessageHandler handler, StorePlatformOptions options, ILogger logger)
		: this(new HttpClient(handler, disposeHandler: false), true, options, logger)
	{
	}

	private PlatformLicenseClient(HttpClient http, bool ownsClient, StorePlatformOptions options, ILogger logger)
	{
		_http = http;
		_ownsClient = ownsClient;
		_baseUrl = options.BaseUrl;
		_logger = logger.ForContext<PlatformLicenseClient>();
	}

	public static bool IsSupported(CompanionLicenseProof proof)
		=> proof.ProductId == CompanionLicenseTokens.Product &&
			(proof.Platform is CompanionLicenseSources.GooglePlay or CompanionLicenseSources.AppStore ||
				proof is { Platform: CompanionLicenseSources.AppStoreLegacy, LegacyKind: CompanionLicenseSources.AppTransactionKind });

	public async Task<PlatformLicenseIssueResult> IssueCompanionLicenseAsync(CompanionLicenseProof proof,
		CancellationToken cancellationToken)
	{
		if (!IsSupported(proof))
		{
			return new PlatformLicenseIssueResult.Refused("unsupported-source");
		}

		using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, IssuePath))
		{
			Content = JsonContent.Create(new IssueRequest(proof.Platform,
					proof.ProductId,
					proof.PurchaseToken,
					proof.OrderId,
					proof.PackageName,
					proof.TransactionId,
					proof.SignedPayload,
					proof.Platform == CompanionLicenseSources.AppStoreLegacy ? proof.LegacyKind : null),
				options: Json)
		};

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(RequestTimeout);
		try
		{
			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			var result = await InterpretIssueAsync(response, timeout.Token);
			_logger.Information("The Macro Deck Platform answered a Companion license request with {Status}: {Result}",
				(int)response.StatusCode,
				Describe(result));
			return result;
		}
		catch (Exception ex) when (IsTransient(ex, cancellationToken))
		{
			_logger.Warning("Requesting a Companion license from the Macro Deck Platform failed: {Error}", ex.GetType().Name);
			return new PlatformLicenseIssueResult.Retry(null, false);
		}
	}

	public async Task<IReadOnlyList<string>?> GetRevokedLicenseIdsAsync(CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(RequestTimeout);
		try
		{
			using var response = await _http.GetAsync(new Uri(_baseUrl, RevocationsPath),
				HttpCompletionOption.ResponseHeadersRead,
				timeout.Token);
			if (response.StatusCode != HttpStatusCode.OK)
			{
				_logger.Warning("The Macro Deck Platform answered the Companion license revocation list with {Status}",
					(int)response.StatusCode);
				return null;
			}

			var body = await response.Content.ReadFromJsonAsync<RevocationList>(Json, timeout.Token);
			if (body?.RevokedLicenseIds is not { } ids)
			{
				return null;
			}

			if (ids.Count > MaximumRevokedLicenseIds)
			{
				_logger.Warning(
					"The Companion license revocation list has {Count} ids, more than the {Maximum} this host accepts; keeping the previous copy",
					ids.Count,
					MaximumRevokedLicenseIds);
				return null;
			}

			return ids.OfType<string>().Where(id => LicenseId().IsMatch(id)).Distinct(StringComparer.Ordinal).ToList();
		}
		catch (Exception ex) when (IsTransient(ex, cancellationToken))
		{
			_logger.Warning("Fetching the Companion license revocation list failed: {Error}", ex.GetType().Name);
			return null;
		}
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private static async Task<PlatformLicenseIssueResult> InterpretIssueAsync(HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		var retryAfter = RetryAfter(response);
		switch (response.StatusCode)
		{
			case HttpStatusCode.OK:
				var issued = await response.Content.ReadFromJsonAsync<IssueResponse>(Json, cancellationToken);
				return string.IsNullOrWhiteSpace(issued?.License)
					? new PlatformLicenseIssueResult.Retry(retryAfter, false)
					: new PlatformLicenseIssueResult.Issued(issued.License);
			case HttpStatusCode.BadRequest:
				return new PlatformLicenseIssueResult.Refused("invalid-proof");
			case HttpStatusCode.RequestEntityTooLarge:
				return new PlatformLicenseIssueResult.Refused("proof-too-large");
			case HttpStatusCode.Forbidden:
				var forbidden = await CodeAsync(response, cancellationToken);
				return forbidden == "license-revoked"
					? new PlatformLicenseIssueResult.Refused("license-revoked")
					: new PlatformLicenseIssueResult.Retry(retryAfter, false, forbidden);
			case HttpStatusCode.UnprocessableEntity:
				var code = await CodeAsync(response, cancellationToken);
				return code is not null && RetryableRefusals.Contains(code)
					? new PlatformLicenseIssueResult.Retry(retryAfter, true, code)
					: new PlatformLicenseIssueResult.Refused(code ?? "purchase-refused");
			default:
				return new PlatformLicenseIssueResult.Retry(retryAfter, false, await CodeAsync(response, cancellationToken));
		}
	}

	private static async Task<string?> CodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
				cancellationToken: cancellationToken);
			return problem.RootElement.ValueKind == JsonValueKind.Object &&
				problem.RootElement.TryGetProperty("code", out var code) &&
				code.ValueKind == JsonValueKind.String &&
				code.GetString() is { } value &&
				ProblemCode().IsMatch(value)
					? value
					: null;
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

	private static bool IsTransient(Exception ex, CancellationToken cancellationToken)
		=> ex is HttpRequestException or IOException or JsonException or NotSupportedException ||
			ex is OperationCanceledException && !cancellationToken.IsCancellationRequested;

	private static string Describe(PlatformLicenseIssueResult result)
		=> result switch
		{
			PlatformLicenseIssueResult.Issued => "issued",
			PlatformLicenseIssueResult.Refused refused => $"refused ({refused.Code})",
			PlatformLicenseIssueResult.Retry { PurchasePending: true } => "purchase pending, retrying",
			_ => "retrying"
		};

	[GeneratedRegex("^[0-9a-f]{32}$")]
	private static partial Regex LicenseId();

	[GeneratedRegex("^[a-z][a-z-]{0,63}$")]
	private static partial Regex ProblemCode();

	private sealed record IssueRequest(
		string? Platform,
		string? ProductId,
		string? PurchaseToken,
		string? OrderId,
		string? PackageName,
		string? TransactionId,
		string? SignedPayload,
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LegacyKind);

	private sealed record IssueResponse(string? License);

	private sealed record RevocationList(List<string?>? RevokedLicenseIds);
}

public static class CompanionLicenseSources
{
	public const string GooglePlay = "google-play";
	public const string AppStore = "app-store";
	public const string AppStoreLegacy = "app-store-legacy";
	public const string AppTransactionKind = "appTransaction";
}
