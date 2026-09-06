using System.Net.Http.Headers;
using System.Text;

namespace MacroDeckHost.Integrations.Http.Client;

internal readonly struct HttpMessageBuildResult
{
	private HttpMessageBuildResult(HttpRequestMessage? message, HttpFailureKind? failure)
	{
		Message = message;
		Failure = failure;
	}

	public HttpRequestMessage? Message { get; }

	public HttpFailureKind? Failure { get; }

	public static HttpMessageBuildResult Success(HttpRequestMessage message) => new(message, null);

	public static HttpMessageBuildResult Failed(HttpFailureKind failure) => new(null, failure);
}

internal static class HttpRequestFactory
{
	public static HttpMessageBuildResult CreateMessage(HttpRequestSpec spec)
	{
		var message = new HttpRequestMessage(new HttpMethod(spec.Method), spec.Uri);

		var (content, failure) = CreateContent(spec.Body);
		if (failure is { } kind)
		{
			message.Dispose();
			return HttpMessageBuildResult.Failed(kind);
		}

		message.Content = content;
		ApplyHeaders(message, spec.Headers);
		ApplyAuth(message, spec.Auth);
		return HttpMessageBuildResult.Success(message);
	}

	private static (HttpContent? Content, HttpFailureKind? Failure) CreateContent(HttpBody body)
	{
		switch (body)
		{
			case HttpBodyNone:
				return (null, null);

			case HttpBodyJson json:
			{
				var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json.Json));
				content.Headers.ContentType =
					MediaTypeHeaderValue.Parse(json.ContentType ?? "application/json; charset=utf-8");
				return (content, null);
			}

			case HttpBodyText text:
			{
				var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text.Text));
				content.Headers.ContentType
					= MediaTypeHeaderValue.Parse(text.ContentType ?? "text/plain; charset=utf-8");
				return (content, null);
			}

			case HttpBodyForm form:
			{
				var content = new FormUrlEncodedContent(form.Fields);
				if (form.ContentType is not null)
				{
					content.Headers.ContentType = MediaTypeHeaderValue.Parse(form.ContentType);
				}

				return (content, null);
			}

			case HttpBodyMultipart multipart:
				return CreateMultipartContent(multipart);

			default:
				throw new NotSupportedException($"Unsupported HTTP body type '{body.GetType()}'.");
		}
	}

	private static (HttpContent? Content, HttpFailureKind? Failure) CreateMultipartContent(HttpBodyMultipart multipart)
	{
		var content = new MultipartFormDataContent();
		foreach (var (name, value) in multipart.Fields)
		{
			content.Add(new StringContent(value), name);
		}

		FileStream fileStream;
		try
		{
			fileStream = File.OpenRead(multipart.FilePath);
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
		{
			content.Dispose();
			return (null, HttpFailureKind.FileMissing);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException
			or IOException
			or ArgumentException
			or NotSupportedException)
		{
			content.Dispose();
			return (null, HttpFailureKind.FileUnreadable);
		}

		try
		{
			var streamContent = new StreamContent(fileStream);
			streamContent.Headers.ContentType =
				new MediaTypeHeaderValue(HttpFileContentTypes.Resolve(multipart.FilePath));
			content.Add(streamContent, multipart.FileFieldName, Path.GetFileName(multipart.FilePath));
		}
		catch (Exception ex) when (ex is ArgumentException or FormatException)
		{
			fileStream.Dispose();
			content.Dispose();
			return (null, HttpFailureKind.FileUnreadable);
		}

		return (content, null);
	}

	private static void ApplyHeaders(HttpRequestMessage message, IReadOnlyDictionary<string, string> headers)
	{
		foreach (var (name, value) in headers)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			if (message.Headers.TryAddWithoutValidation(name, value))
			{
				continue;
			}

			if (message.Content is null)
			{
				continue;
			}

			message.Content.Headers.Remove(name);
			message.Content.Headers.TryAddWithoutValidation(name, value);
		}
	}

	private static void ApplyAuth(HttpRequestMessage message, HttpAuth auth)
	{
		switch (auth)
		{
			case HttpAuthBasic basic:
				var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basic.Username}:{basic.Secret}"));
				message.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
				break;

			case HttpAuthBearer bearer:
				message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Token);
				break;

			case HttpAuthHeader header:
				message.Headers.Remove(header.HeaderName);
				message.Headers.TryAddWithoutValidation(header.HeaderName, header.Value);
				break;

			case HttpAuthNone:
				break;
		}
	}
}
