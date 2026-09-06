using MacroDeck.Plugin.Analyzers;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Icons;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Hosting.Capabilities.Icons;

/// <summary>
/// Exposes the icon the manifest names as the <c>icons</c> capability. Unlike every other kind, this
/// declares metadata only - the local id is the literal <c>icon</c>, not the provider-shaped kinds'
/// <c>provider</c> convention, and <c>describe</c> is the only operation there is. The bytes themselves
/// never travel through this handler: <see cref="IconAssetSource" /> pushes them over the <c>asset.*</c>
/// pipeline separately, at connect, because an asset is chunked and a capability response is not.
///
/// <para>
/// Registered only when the manifest actually declares a usable icon - see <c>PluginHostBuilder</c>'s
/// SDK service registration. A plugin without one declares no <c>icons</c> capability at all.
/// </para>
/// </summary>
internal sealed class IconsCapabilityHandler(IconAssetSource iconSource) : ICapabilityHandler
{
	/// <summary>The one local id this kind ever declares - see the class remarks.</summary>
	public const string LocalId = "icon";

	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	public string Kind => CapabilityKinds.Icons;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> iconSource.HasIcon
			? [new DeclaredCapability { Kind = CapabilityKinds.Icons, LocalId = LocalId, VersionRange = _version }]
			: [];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely, exactly like every other kind's single-operation
		// describe (see EventsCapabilityHandler's identical remark) - RemotePluginSnapshotRefresher always
		// sends the "*" placeholder for it, never the declared "icon" id, so gating on it here would
		// silently break every registration's describe round trip.
		if (!string.Equals(invocation.Operation, CapabilityOperations.Icons.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The icons capability has no operation '{invocation.Operation}'."));
		}

		if (!iconSource.HasIcon)
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No icon provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return Task.FromResult(CapabilityInvocationResult.Ok(new IconsDescribePayload
		{
			MimeType = iconSource.MimeType, ByteLength = iconSource.Bytes.Length, ContentHash = iconSource.ContentHash
		}));
	}
}

/// <summary>
/// Reads the manifest's icon off disk once, at <c>Build</c>, and caches the bytes, mime type and content
/// hash. Reading it once is the whole contract: a plugin's icon is a file the manifest names, so nothing
/// about the answer can legitimately change within a process lifetime.
/// Shared between <see cref="IconsCapabilityHandler" /> (which answers <c>describe</c> from it) and
/// <see cref="IconAssetPublisherHostedService" /> (which uploads the same bytes over <c>asset.*</c>) so
/// the content hash both sides agree on is always the hash of the exact bytes that get pushed.
/// </summary>
internal sealed class IconAssetSource
{
	/// <summary>The plugin declares no usable icon: no capability, no upload, nothing on the wire.</summary>
	public static readonly IconAssetSource None = new([], string.Empty);

	internal IconAssetSource(byte[] bytes, string mimeType)
	{
		Bytes = bytes;
		MimeType = mimeType;
		ContentHash = bytes.Length > 0 ? AssetContentHash.Compute(bytes) : string.Empty;
	}

	/// <summary>
	/// Resolves the manifest's <c>icon</c> into bytes, appending anything wrong with it to the same
	/// <paramref name="problems" /> list <c>Build</c> collects everything else into - so an icon a plugin
	/// cannot serve is reported next to every other configuration problem, rather than as an upload
	/// failure minutes later in a log.
	///
	/// <para>
	/// An unsafe or missing path is skipped <em>silently</em>: <c>BuildMetadata</c> has already reported
	/// both, and repeating them would say the same thing twice. Skipping rather than reading also means
	/// an unsafe path is never actually opened. The extension check does run first and does report,
	/// because it is a property of the manifest value alone - <c>"icon": "missing.bmp"</c> is two
	/// genuinely different mistakes, and an author fixing only the path would otherwise hit the second
	/// one on the next build.
	/// </para>
	/// </summary>
	public static IconAssetSource Create(PluginMetadata metadata, string contentRootPath, List<string> problems)
	{
		ArgumentNullException.ThrowIfNull(metadata);
		ArgumentNullException.ThrowIfNull(problems);
		ArgumentNullException.ThrowIfNull(contentRootPath);

		if (metadata.IconPath is not { Length: > 0 } iconPath)
		{
			return None;
		}

		// Before the disk checks: the extension is a property of the manifest value alone, so it is worth
		// reporting even when the file it names is also missing.
		var mimeType = IconMediaTypes.ForExtension(iconPath);
		if (mimeType is null)
		{
			problems.Add($"The manifest's icon '{iconPath}' is not a supported image type. " +
				$"Use one of {string.Join(", ", IconMediaTypes.SupportedExtensions)}.");
			return None;
		}

		if (!PluginManifestFileReader.IsSafeRelativeIconPath(iconPath))
		{
			return None;
		}

		var resolved = Path.Combine(contentRootPath, iconPath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(resolved))
		{
			return None;
		}

		byte[] bytes;
		try
		{
			bytes = File.ReadAllBytes(resolved);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			problems.Add($"The manifest's icon '{iconPath}' could not be read: {exception.Message}");
			return None;
		}

		if (bytes.Length == 0)
		{
			problems.Add($"The manifest's icon '{iconPath}' is empty.");
			return None;
		}

		if (bytes.Length > ProtocolLimits.MaxAssetBytes)
		{
			problems.Add($"The manifest's icon '{iconPath}' is {bytes.Length} bytes, over the " +
				$"{ProtocolLimits.MaxAssetBytes} byte limit for an asset.");
			return None;
		}

		return new IconAssetSource(bytes, mimeType);
	}

	public bool HasIcon => Bytes.Length > 0;

	public byte[] Bytes { get; }

	public string MimeType { get; }

	public string ContentHash { get; }
}
