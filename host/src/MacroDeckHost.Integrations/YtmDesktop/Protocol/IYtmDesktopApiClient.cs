using System.Net;
using System.Text.Json;

namespace MacroDeckHost.Integrations.YtmDesktop.Protocol;

internal interface IYtmDesktopApiClient : IDisposable
{
	void UseToken(string? token);

	Task<IReadOnlyList<string>> GetApiVersionsAsync(CancellationToken cancellationToken);

	Task<string> RequestAuthCodeAsync(
		string appId,
		string appName,
		string appVersion,
		CancellationToken cancellationToken);

	Task<string> RequestTokenAsync(string appId, string code, CancellationToken cancellationToken);

	Task<JsonElement> GetStateAsync(CancellationToken cancellationToken);

	Task<IReadOnlyList<YtmPlaylist>> GetPlaylistsAsync(CancellationToken cancellationToken);

	Task SendCommandAsync(string command, object? data, CancellationToken cancellationToken);
}

internal class YtmDesktopApiException : Exception
{
	public YtmDesktopApiException(
		string message,
		HttpStatusCode statusCode,
		string? errorCode = null,
		TimeSpan? retryAfter = null)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorCode = errorCode;
		RetryAfter = retryAfter;
	}

	public YtmDesktopApiException(string message, Exception innerException, HttpStatusCode statusCode)
		: base(message, innerException)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }

	public string? ErrorCode { get; }

	public TimeSpan? RetryAfter { get; }
}

internal sealed class YtmDesktopAuthorizationException : YtmDesktopApiException
{
	public YtmDesktopAuthorizationException(string message)
		: base(message, HttpStatusCode.Unauthorized, YtmErrorCodes.Unauthenticated)
	{
	}
}
