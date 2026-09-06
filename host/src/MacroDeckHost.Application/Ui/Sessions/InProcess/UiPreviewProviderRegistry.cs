using System.Collections.Concurrent;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

public sealed class UiPreviewProviderRegistry
{
	private const string Prefix = "ui-preview:";

	private readonly IEnumerable<IUiPreviewSource> _sources;
	private readonly Func<IUiSessionSink> _sink;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, InProcessUiSessionProvider> _adapters = new(StringComparer.Ordinal);

	public UiPreviewProviderRegistry(IEnumerable<IUiPreviewSource> sources, Func<IUiSessionSink> sink, ILogger logger)
	{
		_sources = sources;
		_sink = sink;
		_logger = logger;
	}

	public static string ProviderIdFor(string previewId, string principal) => $"{Prefix}{previewId}:{principal}";

	public IUiSessionProvider? Resolve(string providerId)
	{
		if (!TryParse(providerId, out var previewId))
		{
			return null;
		}

		var source = _sources.FirstOrDefault(candidate =>
			candidate.Previews.Any(preview => string.Equals(preview.Id, previewId, StringComparison.Ordinal)));

		if (source is null)
		{
			return null;
		}

		return _adapters.GetOrAdd(providerId,
			static (id, state) => new InProcessUiSessionProvider(id, state.Source, state.Sink(), state.Logger),
			(Source: source, Sink: _sink, Logger: _logger));
	}

	// The preview id itself carries colons (see UiPreviewCatalog.Id), so the principal - never containing
	// one - is split off from the right rather than the left.
	private static bool TryParse(string providerId, out string previewId)
	{
		previewId = string.Empty;

		if (!providerId.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return false;
		}

		var rest = providerId[Prefix.Length..];
		var separator = rest.LastIndexOf(':');
		if (separator < 0)
		{
			return false;
		}

		previewId = rest[..separator];
		return previewId.Length > 0;
	}
}
