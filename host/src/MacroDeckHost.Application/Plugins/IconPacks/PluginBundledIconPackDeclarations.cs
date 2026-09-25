using System.Collections.Concurrent;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Plugins.Runtime;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Plugins.IconPacks;

public sealed record DeclaredIconPack(string Key, string Revision, Func<Stream> Open);

public sealed record DeclaredIconPackSet(string PluginId, string PluginName, bool Development, IReadOnlyList<DeclaredIconPack> Packs)
{
	public bool Declares(string key) => Packs.Any(pack => string.Equals(pack.Key, key, StringComparison.Ordinal));
}

public sealed record DevelopmentIconPack(string Key, string ContentHash, byte[] Content);

public interface IPluginBundledIconPackDeclarations
{
	DeclaredIconPackSet? Find(string pluginId);

	void SetDevelopment(string pluginId, string sessionId, string pluginName, IReadOnlyList<DevelopmentIconPack> packs);

	bool ClearDevelopment(string pluginId, string sessionId);

	void Invalidate(string pluginId);
}

public sealed class PluginBundledIconPackDeclarations(
	IPluginInstallationCatalog installationCatalog,
	IPluginManifestReader manifestReader,
	ILogger logger) : IPluginBundledIconPackDeclarations
{
	private readonly ConcurrentDictionary<string, (string SessionId, DeclaredIconPackSet Set)> _development =
		new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, (string Version, DeclaredIconPackSet? Set)> _installed =
		new(StringComparer.Ordinal);

	private readonly ILogger _logger = logger.ForContext<PluginBundledIconPackDeclarations>();

	public DeclaredIconPackSet? Find(string pluginId)
	{
		if (_development.TryGetValue(pluginId, out var development))
		{
			return development.Set;
		}

		if (!installationCatalog.TryResolveActive(pluginId, out var active) || active is null)
		{
			return null;
		}

		if (_installed.TryGetValue(pluginId, out var cached) && string.Equals(cached.Version, active.Version, StringComparison.Ordinal))
		{
			return cached.Set;
		}

		var set = ReadInstalled(pluginId, active);
		_installed[pluginId] = (active.Version, set);
		return set;
	}

	public void SetDevelopment(string pluginId, string sessionId, string pluginName, IReadOnlyList<DevelopmentIconPack> packs)
		=> _development[pluginId] = (sessionId,
			new DeclaredIconPackSet(pluginId,
				pluginName,
				Development: true,
				packs.Select(pack => new DeclaredIconPack(pack.Key, pack.ContentHash, () => new MemoryStream(pack.Content, writable: false)))
					.ToList()));

	public bool ClearDevelopment(string pluginId, string sessionId)
		=> _development.TryGetValue(pluginId, out var current) &&
			string.Equals(current.SessionId, sessionId, StringComparison.Ordinal) &&
			_development.TryRemove(new KeyValuePair<string, (string, DeclaredIconPackSet)>(pluginId, current));

	public void Invalidate(string pluginId) => _installed.TryRemove(pluginId, out _);

	private DeclaredIconPackSet? ReadInstalled(string pluginId, InstalledPluginVersion active)
	{
		var manifest = manifestReader.Read(active.ManifestPath, pluginId, active.Version).Manifest;
		if (manifest is null)
		{
			return null;
		}

		var root = Path.GetFullPath(active.VersionDirectory);
		var packs = new List<DeclaredIconPack>();
		foreach (var declared in manifest.BundledIconPacks ?? [])
		{
			// Only a file the signature covers may be read: the key to path mapping sits outside the digest.
			var signed = manifest.Files?.FirstOrDefault(file => string.Equals(file.Path, declared.Path, StringComparison.Ordinal));
			var path = Path.GetFullPath(Path.Combine(root, declared.Path));
			if (signed is null || !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			{
				_logger.Warning("Plugin {PluginId} declares bundled icon pack {Key} at {Path}, which its signed files do not list; skipping",
					pluginId,
					declared.Key,
					declared.Path);
				continue;
			}

			packs.Add(new DeclaredIconPack(declared.Key, signed.Sha256, () => File.OpenRead(path)));
		}

		return new DeclaredIconPackSet(pluginId, manifest.Name, Development: false, packs);
	}
}
