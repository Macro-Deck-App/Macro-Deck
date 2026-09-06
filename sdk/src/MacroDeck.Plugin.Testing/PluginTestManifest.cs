using System.Text.Json;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// A generated <c>manifest.json</c> for a plugin under test, written into a fresh, owned content root
/// directory that <see cref="Apply" /> points a <see cref="PluginHostBuilder" /> at.
///
/// <para>
/// <c>PluginHostBuilder.Build()</c> has required a manifest at the content root since #522 - a plugin's
/// id, name, version, description and icon all come from there now, never from builder calls. Every
/// in-process entry point this package offers (<see cref="PluginTestHarness.Create" />,
/// <see cref="PluginTestHarness.ProblemsOf" />, <see cref="MacroDeckTestHost.HostAsync" />, and
/// <c>ConformanceSubject.InProcess</c> through it) applies a default instance of this automatically when
/// the caller does not supply its own, so "just build me a working plugin" stays a single call with no
/// JSON literal at the call site. Construct one explicitly only when a test is about a specific id, name,
/// version, description or icon - asserting that <c>/_macrodeck/info</c> reports a particular name and
/// version, or that a conformance check's manifest-derived content is what is under test.
/// </para>
///
/// <para>
/// Every field left <see langword="null" /> - including <see cref="Id" /> - gets a sensible default, so
/// this type never has to be told anything to produce a plugin that builds. <see cref="Id" /> defaults to
/// a fresh, valid reverse-domain id unique to this instance (the same shape
/// <see cref="PluginTestCredentials.Managed" /> generates for its own identity), so two defaulted
/// manifests never collide even when a test builds several plugins in the same run. Nothing here
/// validates its inputs beyond that - an invalid override (an id that fails
/// <c>MacroDeckId.TryValidateOwnerId</c>, an empty name) is written verbatim, because a test proving that
/// <see cref="PluginTestHarness.ProblemsOf" /> reports exactly that problem needs to be able to construct
/// it in the first place.
/// </para>
/// </summary>
public sealed class PluginTestManifest : IDisposable
{
	private const string ManifestFileName = "manifest.json";

	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	// Composes TempStateDirectory's own create-and-delete idiom rather than reimplementing it - this type
	// is not itself a plugin's persisted *state*, but the lifetime shape (a fresh, uniquely named temp
	// directory, deleted on dispose, best-effort) is identical.
	private readonly TempStateDirectory _contentRoot = new();

	private bool _disposed;

	/// <summary>
	/// Generates a manifest, defaulting whichever fields are omitted: <paramref name="name" /> to
	/// <c>"Test Plugin"</c>, <paramref name="version" /> to <c>"1.0.0"</c>, and <paramref name="id" /> to a
	/// fresh reverse-domain id. <paramref name="description" /> and <paramref name="icon" /> stay unset
	/// (null) when omitted, matching a real manifest where both are optional.
	/// </summary>
	public PluginTestManifest(
		string? id = null,
		string? name = null,
		string? version = null,
		string? description = null,
		string? icon = null)
	{
		Id = id ?? PluginTestCredentials.GenerateOwnerId();
		Name = name ?? "Test Plugin";
		Version = version ?? "1.0.0";
		Description = description;
		Icon = icon;

		var document = new ManifestDocument
		{
			ManifestVersion = PluginManifest.SupportedManifestVersion,
			Id = Id,
			Name = Name,
			Version = Version,
			Description = Description,
			Icon = Icon
		};

		File.WriteAllText(Path.Combine(_contentRoot.Path, ManifestFileName),
			JsonSerializer.Serialize(document, _jsonOptions));
	}

	/// <summary>The content root directory this manifest was written into.</summary>
	public string ContentRoot => _contentRoot.Path;

	/// <summary>The id this manifest declares - generated when the constructor's <c>id</c> parameter was omitted.</summary>
	public string Id { get; }

	/// <summary>The name this manifest declares - defaulted to <c>"Test Plugin"</c> when omitted.</summary>
	public string Name { get; }

	/// <summary>The version this manifest declares - defaulted to <c>"1.0.0"</c> when omitted.</summary>
	public string Version { get; }

	/// <summary>The description this manifest declares, or null when none was given.</summary>
	public string? Description { get; }

	/// <summary>The icon path this manifest declares, or null when none was given.</summary>
	public string? Icon { get; }

	/// <summary>
	/// Points <paramref name="builder" />'s content root at this manifest, so
	/// <see cref="PluginHostBuilder.Build" /> reads it as the plugin's identity. Must run before
	/// <see cref="PluginHostBuilder.Build" /> is called.
	/// </summary>
	public void Apply(PluginHostBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Environment.ContentRootPath = ContentRoot;
	}

	/// <summary>Deletes the content root directory this manifest was written into.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_contentRoot.Dispose();
	}

	/// <summary>The subset of manifest.json this type writes - deliberately not the packaging package's own
	/// <see cref="PluginManifest" />, which requires entrypoints a plugin under test never has.</summary>
	private sealed record ManifestDocument
	{
		public required int ManifestVersion { get; init; }

		public required string Id { get; init; }

		public required string Name { get; init; }

		public required string Version { get; init; }

		public string? Description { get; init; }

		public string? Icon { get; init; }
	}
}
