using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Store.Reviews;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StorePlatformClient : IStorePlatformClient, IDisposable
{
	public const string HttpClientName = "store-platform";

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly IConnectSessionService _session;
	private readonly StorePlatformOptions _options;
	private readonly ILogger _logger;

	public StorePlatformClient(IHttpClientFactory httpClientFactory,
		IConnectSessionService session,
		StorePlatformOptions options,
		ILogger logger)
		: this(httpClientFactory.CreateClient(HttpClientName), false, session, options, logger)
	{
	}

	internal StorePlatformClient(HttpMessageHandler handler,
		IConnectSessionService session,
		StorePlatformOptions options,
		ILogger logger)
		: this(new HttpClient(handler, disposeHandler: false), true, session, options, logger)
	{
	}

	private StorePlatformClient(HttpClient http,
		bool ownsClient,
		IConnectSessionService session,
		StorePlatformOptions options,
		ILogger logger)
	{
		_http = http;
		_ownsClient = ownsClient;
		_session = session;
		_options = options;
		_logger = logger.ForContext<StorePlatformClient>();
	}

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	public async Task<StorePlatformResult<IReadOnlyDictionary<string, StorePlatformRating>>> GetRatings(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default)
	{
		var merged = new Dictionary<string, StorePlatformRating>(StringComparer.Ordinal);
		foreach (var chunk in Chunks(packageIds))
		{
			var result = await Send<Dictionary<string, StorePlatformRating>>(HttpMethod.Get,
				$"api/v1/store/ratings?packageIds={JoinIds(chunk)}",
				content: null,
				authenticated: false,
				cancellationToken);
			if (!result.Success)
			{
				return StorePlatformResult.Fail<IReadOnlyDictionary<string, StorePlatformRating>>(result.Failure,
					result.RetryAfter,
					result.Field);
			}

			foreach (var (id, rating) in result.Value ?? [])
			{
				merged[id] = rating;
			}
		}

		return StorePlatformResult.Ok<IReadOnlyDictionary<string, StorePlatformRating>>(merged);
	}

	public async Task<StorePlatformResult<IReadOnlyDictionary<string, StorePlatformInstalls>>> GetInstalls(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default)
	{
		var merged = new Dictionary<string, StorePlatformInstalls>(StringComparer.Ordinal);
		foreach (var chunk in Chunks(packageIds))
		{
			var result = await Send<Dictionary<string, StorePlatformInstalls>>(HttpMethod.Get,
				$"api/v1/store/installs?packageIds={JoinIds(chunk)}",
				content: null,
				authenticated: false,
				cancellationToken);
			if (!result.Success)
			{
				return StorePlatformResult.Fail<IReadOnlyDictionary<string, StorePlatformInstalls>>(result.Failure,
					result.RetryAfter,
					result.Field);
			}

			foreach (var (id, installs) in result.Value ?? [])
			{
				merged[id] = installs;
			}
		}

		return StorePlatformResult.Ok<IReadOnlyDictionary<string, StorePlatformInstalls>>(merged);
	}

	public Task<StorePlatformResult<StorePlatformRating>> GetRating(string packageId,
		CancellationToken cancellationToken = default) =>
		Send<StorePlatformRating>(HttpMethod.Get,
			$"{PackagePath(packageId)}/rating",
			content: null,
			authenticated: false,
			cancellationToken);

	public Task<StorePlatformResult<StorePlatformReviewPage>> GetReviews(string packageId,
		int page,
		int pageSize,
		StorePlatformReviewSort sort,
		int? rating,
		CancellationToken cancellationToken = default)
	{
		var query = $"page={page}&pageSize={pageSize}&sort={sort}";
		if (rating is { } stars)
		{
			query += $"&rating={stars}";
		}

		return Send<StorePlatformReviewPage>(HttpMethod.Get,
			$"{PackagePath(packageId)}/reviews?{query}",
			content: null,
			authenticated: false,
			cancellationToken);
	}

	public async Task<StorePlatformResult<StorePlatformOwnReview>> GetOwnReview(string packageId,
		CancellationToken cancellationToken = default)
	{
		var result = await Send<StorePlatformOwnReview>(HttpMethod.Get,
			$"{PackagePath(packageId)}/reviews/me",
			content: null,
			authenticated: true,
			cancellationToken);
		return result.Failure == StorePlatformFailure.NotFound ? StorePlatformResult.Ok<StorePlatformOwnReview>(null) : result;
	}

	public Task<StorePlatformResult<StorePlatformOwnReview>> PutOwnReview(string packageId,
		int rating,
		string? title,
		string? body,
		CancellationToken cancellationToken = default) =>
		Send<StorePlatformOwnReview>(HttpMethod.Put,
			$"{PackagePath(packageId)}/reviews/me",
			JsonContent.Create(new { rating, title, body }, options: _json),
			authenticated: true,
			cancellationToken);

	public async Task<StorePlatformResult<bool>> DeleteOwnReview(string packageId,
		CancellationToken cancellationToken = default)
	{
		var result = await Send<JsonElement?>(HttpMethod.Delete,
			$"{PackagePath(packageId)}/reviews/me",
			content: null,
			authenticated: true,
			cancellationToken);
		return result.Success
			? StorePlatformResult.Ok<bool>(true)
			: StorePlatformResult.Fail<bool>(result.Failure, result.RetryAfter, result.Field);
	}

	public Task<StorePlatformResult<bool>> ReportPackage(string packageId,
		string category,
		string? detail,
		CancellationToken cancellationToken = default) =>
		Report($"{PackagePath(packageId)}/report", category, detail, cancellationToken);

	public Task<StorePlatformResult<bool>> ReportReview(string packageId,
		Guid reviewId,
		string category,
		string? detail,
		CancellationToken cancellationToken = default) =>
		Report($"{PackagePath(packageId)}/reviews/{reviewId:D}/report", category, detail, cancellationToken);

	public async Task<StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementStatus>>> GetEntitlements(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default)
	{
		var merged = new Dictionary<string, StoreEntitlementStatus>(StringComparer.Ordinal);
		foreach (var chunk in Chunks(packageIds))
		{
			var result = await Send<Dictionary<string, string>>(HttpMethod.Get,
				$"api/v1/store/entitlements?packageIds={JoinIds(chunk)}",
				content: null,
				authenticated: true,
				cancellationToken);
			if (!result.Success)
			{
				return StorePlatformResult.Fail<IReadOnlyDictionary<string, StoreEntitlementStatus>>(result.Failure,
					result.RetryAfter,
					result.Field);
			}

			foreach (var (id, status) in result.Value ?? [])
			{
				merged[id] = Enum.TryParse<StoreEntitlementStatus>(status, out var parsed)
					? parsed
					: StoreEntitlementStatus.Unavailable;
			}
		}

		return StorePlatformResult.Ok<IReadOnlyDictionary<string, StoreEntitlementStatus>>(merged);
	}

	public async Task<StorePlatformResult<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>> ClaimEntitlements(
		IReadOnlyCollection<string> packageIds,
		CancellationToken cancellationToken = default)
	{
		var merged = new Dictionary<string, StoreEntitlementClaimStatus>(StringComparer.Ordinal);
		foreach (var chunk in Chunks(packageIds))
		{
			var result = await Send<Dictionary<string, string>>(HttpMethod.Post,
				"api/v1/store/entitlements/claim",
				JsonContent.Create(new { packageIds = chunk }, options: _json),
				authenticated: true,
				cancellationToken);
			if (!result.Success)
			{
				return StorePlatformResult.Fail<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>(result.Failure,
					result.RetryAfter,
					result.Field);
			}

			foreach (var (id, status) in result.Value ?? [])
			{
				merged[id] = Enum.TryParse<StoreEntitlementClaimStatus>(status, out var parsed)
					? parsed
					: StoreEntitlementClaimStatus.Unavailable;
			}
		}

		return StorePlatformResult.Ok<IReadOnlyDictionary<string, StoreEntitlementClaimStatus>>(merged);
	}

	public async Task<StorePlatformResult<IReadOnlyList<StorePlatformTest>>> GetTests(
		CancellationToken cancellationToken = default)
	{
		var result = await Send<List<StorePlatformTest>>(HttpMethod.Get,
			"api/v1/store/tests",
			content: null,
			authenticated: true,
			cancellationToken);
		return result.Success
			? StorePlatformResult.Ok<IReadOnlyList<StorePlatformTest>>(result.Value)
			: StorePlatformResult.Fail<IReadOnlyList<StorePlatformTest>>(result.Failure, result.RetryAfter, result.Field);
	}

	public Task<StorePlatformResult<StorePlatformTestBuildDownload>> GetTestBuildDownload(string packageId,
		Guid buildId,
		CancellationToken cancellationToken = default) =>
		Send<StorePlatformTestBuildDownload>(HttpMethod.Post,
			$"api/v1/store/tests/{Uri.EscapeDataString(packageId)}/builds/{buildId:D}/download",
			content: null,
			authenticated: true,
			cancellationToken);

	private async Task<StorePlatformResult<bool>> Report(string relativeUrl,
		string category,
		string? detail,
		CancellationToken cancellationToken)
	{
		var result = await Send<JsonElement?>(HttpMethod.Post,
			relativeUrl,
			JsonContent.Create(new { category, detail }, options: _json),
			authenticated: true,
			cancellationToken);
		if (result.Success)
		{
			return StorePlatformResult.Ok(true);
		}

		var failure = result.Failure == StorePlatformFailure.Moderated
			? StorePlatformFailure.AlreadyReported
			: result.Failure;
		return StorePlatformResult.Fail<bool>(failure, result.RetryAfter, result.Field);
	}

	private async Task<StorePlatformResult<T>> Send<T>(HttpMethod method,
		string relativeUrl,
		HttpContent? content,
		bool authenticated,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, new Uri(_options.BaseUrl, relativeUrl)) { Content = content };

		if (authenticated)
		{
			if (_session.Current.Status != ConnectAccountStatus.SignedIn)
			{
				return StorePlatformResult.Fail<T>(StorePlatformFailure.SignInRequired);
			}

			try
			{
				var token = await _session.GetAccessToken(cancellationToken);
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			}
			catch (ConnectAuthRejectedException)
			{
				return StorePlatformResult.Fail<T>(StorePlatformFailure.SignInRequired);
			}
			catch (ConnectAccountSuspendedException)
			{
				return StorePlatformResult.Fail<T>(StorePlatformFailure.AccountSuspended);
			}
			catch (ConnectAuthTransientException ex)
			{
				return StorePlatformResult.Fail<T>(StorePlatformFailure.Unavailable, ex.RetryAfter);
			}
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_options.RequestTimeout);

		try
		{
			using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			return await Interpret<T>(response, method, authenticated, timeout.Token);
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or NotSupportedException ||
			ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
		{
			_logger.Warning("The Macro Deck Platform request {Method} {Path} failed: {Error}",
				method.Method,
				request.RequestUri!.AbsolutePath,
				ex.GetType().Name);
			return StorePlatformResult.Fail<T>(StorePlatformFailure.Unavailable);
		}
	}

	private async Task<StorePlatformResult<T>> Interpret<T>(HttpResponseMessage response,
		HttpMethod method,
		bool authenticated,
		CancellationToken cancellationToken)
	{
		var retryAfter = RetryAfter(response);

		if (response.IsSuccessStatusCode)
		{
			if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(JsonElement?))
			{
				return StorePlatformResult.Ok<T>(default);
			}

			var value = await response.Content.ReadFromJsonAsync<T>(_json, cancellationToken);
			return value is null
				? StorePlatformResult.Fail<T>(StorePlatformFailure.Unavailable)
				: StorePlatformResult.Ok<T>(value);
		}

		var problem = await ReadProblem(response, cancellationToken);
		var failure = response.StatusCode switch
		{
			HttpStatusCode.BadRequest => StorePlatformFailure.Validation,
			HttpStatusCode.Forbidden when problem.Flag("accountSuspended") => StorePlatformFailure.AccountSuspended,
			HttpStatusCode.Forbidden when problem.Flag("downloadRequired") => StorePlatformFailure.DownloadRequired,
			HttpStatusCode.Forbidden => StorePlatformFailure.Forbidden,
			HttpStatusCode.NotFound => StorePlatformFailure.NotFound,
			HttpStatusCode.Conflict => StorePlatformFailure.Moderated,
			HttpStatusCode.Gone => StorePlatformFailure.Gone,
			HttpStatusCode.TooManyRequests => StorePlatformFailure.Cooldown,
			HttpStatusCode.ServiceUnavailable => StorePlatformFailure.RetryLater,
			_ => StorePlatformFailure.Unavailable
		};

		// A signed-in session whose token the Platform refuses is a configuration or clock problem, not
		// something the user can fix by signing in again.
		if (response.StatusCode == HttpStatusCode.Unauthorized || failure == StorePlatformFailure.Unavailable)
		{
			_logger.Warning("The Macro Deck Platform answered {Status} to {Method} {Path} (authenticated: {Authenticated})",
				(int)response.StatusCode,
				method.Method,
				response.RequestMessage?.RequestUri?.AbsolutePath,
				authenticated);
		}

		return StorePlatformResult.Fail<T>(failure, retryAfter, failure == StorePlatformFailure.Validation ? problem.Field : null);
	}

	private static async Task<Problem> ReadProblem(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			var text = await response.Content.ReadAsStringAsync(cancellationToken);
			if (string.IsNullOrWhiteSpace(text))
			{
				return Problem.Empty;
			}

			using var document = JsonDocument.Parse(text);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return Problem.Empty;
			}

			var flags = new HashSet<string>(StringComparer.Ordinal);
			string? field = null;
			foreach (var property in document.RootElement.EnumerateObject())
			{
				if (property.Value.ValueKind == JsonValueKind.True)
				{
					flags.Add(property.Name);
				}

				if (property.Name == "errors" && property.Value.ValueKind == JsonValueKind.Object)
				{
					field = property.Value.EnumerateObject().Select(error => error.Name).FirstOrDefault();
				}
			}

			return new Problem(flags, field);
		}
		catch (JsonException)
		{
			return Problem.Empty;
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

		return header.Date is { } date && date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : null;
	}

	private static string PackagePath(string packageId) => $"api/v1/store/packages/{Uri.EscapeDataString(packageId)}";

	private static string JoinIds(IEnumerable<string> ids) => string.Join(',', ids.Select(Uri.EscapeDataString));

	private static IEnumerable<string[]> Chunks(IReadOnlyCollection<string> ids) =>
		ids.Distinct(StringComparer.Ordinal).Chunk(StorePlatformOptions.MaxIdsPerRequest);

	private sealed record Problem(HashSet<string> Flags, string? Field)
	{
		public static readonly Problem Empty = new([], null);

		public bool Flag(string name) => Flags.Contains(name);
	}
}
