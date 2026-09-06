using System.Globalization;

namespace MacroDeckHost.Integrations.Http.Client;

internal abstract record HttpAuth
{
	public static readonly HttpAuth None = new HttpAuthNone();
}

internal sealed record HttpAuthNone : HttpAuth
{
	public override string ToString() => "None";
}

internal sealed record HttpAuthBasic(string Username, string Secret) : HttpAuth
{
	public override string ToString() => "Basic";
}

internal sealed record HttpAuthBearer(string Token) : HttpAuth
{
	public override string ToString() => "Bearer";
}

internal sealed record HttpAuthHeader(string HeaderName, string Value) : HttpAuth
{
	public override string ToString() => $"Header({HeaderName})";
}

internal abstract record HttpBody
{
	public static readonly HttpBody None = new HttpBodyNone();
}

internal sealed record HttpBodyNone : HttpBody
{
	public override string ToString() => "None";
}

internal sealed record HttpBodyJson(string Json, string? ContentType) : HttpBody
{
	public override string ToString() => "Json";
}

internal sealed record HttpBodyText(string Text, string? ContentType) : HttpBody
{
	public override string ToString() => "Text";
}

internal sealed record HttpBodyForm(IReadOnlyDictionary<string, string> Fields, string? ContentType) : HttpBody
{
	public override string ToString()
		=> string.Create(CultureInfo.InvariantCulture, $"Form({Fields.Count} field(s))");
}

internal sealed record HttpBodyMultipart(
	IReadOnlyDictionary<string, string> Fields,
	string FilePath,
	string FileFieldName) : HttpBody
{
	public override string ToString()
		=> string.Create(CultureInfo.InvariantCulture, $"Multipart({Fields.Count} field(s) + file)");
}

internal sealed record HttpRequestSpec(
	string Method,
	Uri Uri,
	IReadOnlyDictionary<string, string> Headers,
	HttpAuth Auth,
	HttpBody Body,
	TimeSpan Timeout,
	bool FollowRedirects,
	bool ValidateTls,
	long MaxResponseBytes)
{
	public override string ToString()
		=> string.Create(CultureInfo.InvariantCulture,
			$"HttpRequestSpec {{ Method = {Method}, Uri = {Uri.Scheme}://{Uri.Host}{Uri.AbsolutePath}, " +
			$"Headers = {Headers.Count} header(s), Auth = {Auth}, Body = {Body}, Timeout = {Timeout}, " +
			$"FollowRedirects = {FollowRedirects}, ValidateTls = {ValidateTls}, " +
			$"MaxResponseBytes = {MaxResponseBytes} }}");
}
