using System.Buffers;
using System.Net;
using System.Text;
using MacroDeckHost.Application.VideoStreams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/video-streams/relay/{token}/{**path}")]
public sealed class VideoStreamRelayController(
	IVideoStreamRelay relay,
	IHttpClientFactory httpClients,
	IHostApplicationLifetime lifetime,
	VideoStreamRelayOptions options) : ControllerBase
{
	private const int TokenLength = 43;
	private const int MaxRedirects = 3;
	private const int CopyBufferSize = 16 * 1024;
	private const int SniffLength = 16;

	// The URL is a capability: an image or video element cannot send an authorization header.
	[HttpGet]
	[HttpHead]
	[AllowAnonymous]
	public async Task<IActionResult> Relay(string token, CancellationToken clientAborted)
	{
		Response.Headers.CacheControl = "no-store";
		Response.Headers.XContentTypeOptions = "nosniff";
		Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";

		if (!IsTokenShape(token) || !TryBuildTarget(token, out var pathAndQuery))
		{
			return NotFound();
		}

		switch (relay.Acquire(token, out var lease))
		{
			case VideoStreamRelayAcquisition.NotFound:
				return NotFound();
			case VideoStreamRelayAcquisition.Busy:
				return StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		using (lease)
		{
			return await ServeAsync(lease!, token, pathAndQuery, clientAborted);
		}
	}

	private async Task<IActionResult> ServeAsync(
		VideoStreamRelayLease lease,
		string token,
		string pathAndQuery,
		CancellationToken clientAborted)
	{
		var origin = lease.Upstream.GetLeftPart(UriPartial.Authority);
		var pinned = new Uri(origin);
		// Concatenated, never resolved against the origin: a path that starts with two slashes would
		// otherwise be read as a network-path reference to another host.
		if (!Uri.TryCreate(origin + pathAndQuery, UriKind.Absolute, out var start) || !SameOrigin(start, pinned))
		{
			return NotFound();
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(
			clientAborted,
			lease.Aborted,
			lifetime.ApplicationStopping);
		var ct = linked.Token;

		Fetch fetch;
		try
		{
			fetch = await FetchAsync(pinned, start, ct);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return clientAborted.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested
				? new EmptyResult()
				: NotFound();
		}
		catch (OperationCanceledException)
		{
			return StatusCode(StatusCodes.Status504GatewayTimeout);
		}
		catch (HttpRequestException)
		{
			return StatusCode(StatusCodes.Status502BadGateway);
		}

		if (fetch.Response is not { } response)
		{
			return StatusCode(StatusCodes.Status502BadGateway);
		}

		using (response)
		{
			try
			{
				return await RespondAsync(response, fetch.Effective, lease, token, pinned, ct);
			}
			catch (Exception e) when (e is OperationCanceledException or IOException or HttpRequestException)
			{
				if (clientAborted.IsCancellationRequested)
				{
					return new EmptyResult();
				}

				if (Response.HasStarted)
				{
					// A cut stream must look broken to the client, not like one that ended cleanly.
					HttpContext.Abort();
					return new EmptyResult();
				}

				return StatusCode(e is OperationCanceledException
					? StatusCodes.Status504GatewayTimeout
					: StatusCodes.Status502BadGateway);
			}
		}
	}

	private async Task<IActionResult> RespondAsync(
		HttpResponseMessage response,
		Uri effective,
		VideoStreamRelayLease lease,
		string token,
		Uri pinned,
		CancellationToken ct)
	{
		var status = (int)response.StatusCode;
		var isHead = HttpMethods.IsHead(Request.Method);
		var contentHeaders = response.Content.Headers;
		var rawContentType = contentHeaders.NonValidated.TryGetValues(HeaderNames.ContentType, out var raw)
			? raw.ToString()
			: null;
		var mediaType = contentHeaders.ContentType?.MediaType;
		var encoding = contentHeaders.ContentEncoding;
		var badEncoding = encoding.Any(value => !string.Equals(value, "identity", StringComparison.OrdinalIgnoreCase));
		if (badEncoding || (rawContentType is not null && mediaType is null))
		{
			return StatusCode(StatusCodes.Status502BadGateway);
		}

		var kind = VideoStreamRelayContent.Classify(
			lease.Transport,
			mediaType,
			VideoStreamRelayContent.IsPlaylistPath(effective.AbsolutePath));
		if (kind == VideoStreamRelayContentKind.Rejected)
		{
			return StatusCode(StatusCodes.Status502BadGateway);
		}

		if (status >= 400)
		{
			return StatusCode(status);
		}

		if (status is not (StatusCodes.Status200OK or StatusCodes.Status206PartialContent))
		{
			return StatusCode(StatusCodes.Status502BadGateway);
		}

		if (isHead)
		{
			if (kind == VideoStreamRelayContentKind.SniffForPlaylistElseRejected)
			{
				return StatusCode(StatusCodes.Status502BadGateway);
			}

			CopyHeaders(response, kind == VideoStreamRelayContentKind.Playlist);
			return new EmptyResult();
		}

		await using var body = await response.Content.ReadAsStreamAsync(ct);
		using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
		var prefix = ReadOnlyMemory<byte>.Empty;

		if (kind is VideoStreamRelayContentKind.SniffForPlaylistElseMedia
			or VideoStreamRelayContentKind.SniffForPlaylistElseRejected)
		{
			var head = new byte[SniffLength];
			var read = await ReadFullyAsync(body, head, idle);
			if (HlsPlaylistRewriter.HasPlaylistSignature(head.AsSpan(0, read)))
			{
				kind = VideoStreamRelayContentKind.Playlist;
				prefix = head.AsMemory(0, read);
			}
			else if (kind == VideoStreamRelayContentKind.SniffForPlaylistElseRejected)
			{
				return StatusCode(StatusCodes.Status502BadGateway);
			}
			else
			{
				kind = VideoStreamRelayContentKind.Media;
				prefix = head.AsMemory(0, read);
			}
		}

		if (kind == VideoStreamRelayContentKind.Playlist)
		{
			if (status == StatusCodes.Status206PartialContent)
			{
				return StatusCode(StatusCodes.Status502BadGateway);
			}

			return await RespondWithPlaylistAsync(body, prefix, effective, token, pinned, ct);
		}

		CopyHeaders(response, false);
		await Response.StartAsync(ct);
		await CopyAsync(body, prefix, idle, ct);
		return new EmptyResult();
	}

	private async Task<IActionResult> RespondWithPlaylistAsync(
		Stream body,
		ReadOnlyMemory<byte> prefix,
		Uri effective,
		string token,
		Uri pinned,
		CancellationToken ct)
	{
		using var buffer = new MemoryStream();
		buffer.Write(prefix.Span);
		var chunk = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
		try
		{
			int read;
			while ((read = await body.ReadAsync(chunk, ct)) > 0)
			{
				if (buffer.Length + read > HlsPlaylistRewriter.MaxPlaylistBytes)
				{
					return StatusCode(StatusCodes.Status502BadGateway);
				}

				buffer.Write(chunk, 0, read);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(chunk);
		}

		var text = new UTF8Encoding(false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
		if (!HlsPlaylistRewriter.TryRewrite(text, token, pinned, effective, out var rewritten))
		{
			return StatusCode(StatusCodes.Status502BadGateway);
		}

		var bytes = new UTF8Encoding(false).GetBytes(rewritten);
		Response.StatusCode = StatusCodes.Status200OK;
		Response.ContentType = VideoStreamRelayContent.PlaylistMediaType;
		Response.ContentLength = bytes.Length;
		await Response.Body.WriteAsync(bytes, ct);
		return new EmptyResult();
	}

	private void CopyHeaders(HttpResponseMessage response, bool rewrittenPlaylist)
	{
		Response.StatusCode = (int)response.StatusCode;
		if (rewrittenPlaylist)
		{
			Response.ContentType = VideoStreamRelayContent.PlaylistMediaType;
			return;
		}

		var content = response.Content.Headers;
		if (content.ContentType is { } contentType)
		{
			Response.ContentType = contentType.ToString();
		}

		if (content.ContentLength is { } length)
		{
			Response.ContentLength = length;
		}

		if (content.ContentRange is { } range)
		{
			Response.Headers.ContentRange = range.ToString();
		}

		if (response.Headers.AcceptRanges.Count > 0)
		{
			Response.Headers.AcceptRanges = string.Join(", ", response.Headers.AcceptRanges);
		}
	}

	private async Task CopyAsync(
		Stream source,
		ReadOnlyMemory<byte> prefix,
		CancellationTokenSource idle,
		CancellationToken ct)
	{
		if (!prefix.IsEmpty)
		{
			await Response.Body.WriteAsync(prefix, ct);
			await Response.Body.FlushAsync(ct);
		}

		var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
		try
		{
			while (true)
			{
				idle.CancelAfter(options.IdleReadTimeout);
				var read = await source.ReadAsync(buffer, idle.Token);
				if (read == 0)
				{
					return;
				}

				await Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
				await Response.Body.FlushAsync(ct);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private async Task<int> ReadFullyAsync(Stream source, byte[] buffer, CancellationTokenSource idle)
	{
		var total = 0;
		while (total < buffer.Length)
		{
			idle.CancelAfter(options.IdleReadTimeout);
			var read = await source.ReadAsync(buffer.AsMemory(total), idle.Token);
			if (read == 0)
			{
				break;
			}

			total += read;
		}

		return total;
	}

	private async Task<Fetch> FetchAsync(Uri pinned, Uri start, CancellationToken ct)
	{
		var http = httpClients.CreateClient(VideoStreamRelay.HttpClientName);
		var method = HttpMethods.IsHead(Request.Method) ? HttpMethod.Head : HttpMethod.Get;
		var range = Request.Headers.Range.ToString();
		var current = start;
		for (var redirects = 0;; redirects++)
		{
			using var request = new HttpRequestMessage(method, current);
			if (range.Length > 0 && !VideoStreamRelayContent.IsPlaylistPath(current.AbsolutePath))
			{
				request.Headers.TryAddWithoutValidation(HeaderNames.Range, range);
			}

			using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			headerTimeout.CancelAfter(options.ResponseHeaderTimeout);
			var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);

			var location = response.Headers.Location;
			if (!IsRedirect(response.StatusCode) || location is null)
			{
				return new Fetch(response, current);
			}

			response.Dispose();
			if (redirects >= MaxRedirects ||
				!Uri.TryCreate(current, location.OriginalString, out var next) ||
				!SameOrigin(next, pinned))
			{
				return new Fetch(null, current);
			}

			current = next;
		}
	}

	private bool TryBuildTarget(string token, out string pathAndQuery)
	{
		pathAndQuery = string.Empty;
		var encoded = Request.Path.ToUriComponent();
		var prefix = VideoStreamRelay.PathPrefix;
		if (encoded.Length < prefix.Length + token.Length ||
			!encoded.AsSpan(prefix.Length).StartsWith(token, StringComparison.Ordinal))
		{
			return false;
		}

		var rest = encoded[(prefix.Length + token.Length)..];
		if (rest.Length == 0)
		{
			rest = "/";
		}

		var query = Request.QueryString.Value ?? string.Empty;
		if (rest[0] != '/' || !IsSafePath(rest) || query.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
		{
			return false;
		}

		pathAndQuery = rest + query;
		return true;
	}

	// Path.ToUriComponent keeps every percent escape it finds, so a request that double-encoded a slash, a
	// backslash or a dot segment still shows it here. That includes a legitimate %252F, which is refused too.
	private static bool IsSafePath(string path)
	{
		foreach (var segment in path.Split('/'))
		{
			var decoded = Uri.UnescapeDataString(segment);
			if (decoded is "." or "..")
			{
				return false;
			}

			foreach (var c in decoded)
			{
				if (c < ' ' || c == '\u007f' || c == '/' || c == '\\')
				{
					return false;
				}
			}
		}

		return true;
	}

	private static bool IsTokenShape(string token)
		=> token.Length == TokenLength && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

	private static bool IsRedirect(HttpStatusCode status)
		=> status is HttpStatusCode.MovedPermanently
			or HttpStatusCode.Found
			or HttpStatusCode.SeeOther
			or HttpStatusCode.TemporaryRedirect
			or HttpStatusCode.PermanentRedirect;

	private static bool SameOrigin(Uri candidate, Uri pinned)
		=> candidate.Scheme == pinned.Scheme &&
			candidate.Port == pinned.Port &&
			string.Equals(candidate.Host, pinned.Host, StringComparison.OrdinalIgnoreCase);

	private readonly record struct Fetch(HttpResponseMessage? Response, Uri Effective);
}
