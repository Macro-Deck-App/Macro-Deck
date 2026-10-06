using MacroDeckHost.Application.Icons.Included;

namespace MacroDeckHost.Infrastructure.IconPacks;

public sealed class EmbeddedIncludedIconAssets : IIncludedIconAssets
{
	private const string ResourcePrefix = "MacroDeckHost.IncludedIcons.";

	public IReadOnlyList<IncludedIconAsset> Load()
	{
		var assembly = typeof(EmbeddedIncludedIconAssets).Assembly;
		var assets = new List<IncludedIconAsset>(IncludedIconPack.Names.Count);
		foreach (var name in IncludedIconPack.Names)
		{
			using var stream = assembly.GetManifestResourceStream(ResourcePrefix + name + ".svg") ??
				throw new InvalidOperationException($"The included icon {name} is not embedded");
			using var buffer = new MemoryStream();
			stream.CopyTo(buffer);
			assets.Add(new IncludedIconAsset(name, buffer.ToArray()));
		}

		return assets;
	}
}
