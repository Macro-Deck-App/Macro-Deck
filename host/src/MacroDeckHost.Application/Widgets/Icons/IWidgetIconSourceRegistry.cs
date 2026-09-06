namespace MacroDeckHost.Application.Widgets.Icons;

/// <summary>Looks up the <see cref="IWidgetIconSource" /> for a reference's provider type.</summary>
public interface IWidgetIconSourceRegistry
{
	/// <summary><c>null</c> when no registered source answers for <paramref name="type" /> - an unknown
	/// provider type resolves to nothing and renders as no icon, exactly as an unparseable legacy id does
	/// today.</summary>
	IWidgetIconSource? Find(string type);
}

public sealed class WidgetIconSourceRegistry : IWidgetIconSourceRegistry
{
	private readonly IReadOnlyDictionary<string, IWidgetIconSource> _sourcesByType;

	public WidgetIconSourceRegistry(IEnumerable<IWidgetIconSource> sources)
	{
		_sourcesByType = sources.ToDictionary(source => source.Type, StringComparer.Ordinal);
	}

	public IWidgetIconSource? Find(string type) => _sourcesByType.GetValueOrDefault(type);
}
