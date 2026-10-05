namespace MacroDeckHost.Application.Portable;

public sealed record PortableWidgetSource(Guid Id, string? Data);

public sealed record PortableAssetBundle(
	IReadOnlyList<PortableIcon> Icons,
	IReadOnlyList<PortableIconFile> Files,
	IReadOnlyList<PortableScript> Scripts,
	IReadOnlyList<PortableSecret> Secrets,
	IReadOnlyList<PortableIntegrationRequirement> Integrations,
	IReadOnlyList<PortableVariable> Variables,
	IReadOnlyList<PortableFont> Fonts,
	IReadOnlyList<PortableFontFile> FontFiles);

public interface IPortableAssetManager
{
	// Takes widget ids (not just widget data) so a new archive kind cannot compile without supplying
	// them - the widget-scoped variables collected here need to know which widget they belong to.
	Task<PortableAssetBundle> Collect(IReadOnlyList<PortableWidgetSource> widgets,
		PortableExportOptions options,
		CancellationToken cancellationToken);

	Task<IReadOnlyDictionary<Guid, Guid>> Import(PortableContent content,
		IReadOnlyList<PortableIconFile> iconFiles,
		IReadOnlyList<PortableFontFile> fontFiles,
		CancellationToken cancellationToken);

	Task RestoreWidgetVariables(PortableContent content,
		IReadOnlyDictionary<Guid, Guid> widgetIdMap,
		CancellationToken cancellationToken);
}
