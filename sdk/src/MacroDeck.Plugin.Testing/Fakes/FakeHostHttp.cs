using MacroDeck.Sdk.Http;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary><see cref="IHostHttp" /> whose <see cref="UserAgent" /> a test sets, starting at the SDK default.</summary>
public sealed class FakeHostHttp : IHostHttp
{
	/// <inheritdoc />
	public string UserAgent { get; set; } = DefaultHostHttp.Instance.UserAgent;
}
