using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Capabilities.Icons;
using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Identity;

namespace MacroDeck.Plugin.Testing.Conformance.Checks;

// MDC01xx - B1: manifest and identifier rules. A malformed plugin id or a malformed *declared* local id
// cannot reach a running subject at all - PluginHostBuilder.Build rejects both before a session can ever
// exist - so MDC0101/MDC0102 are regression guards, read from the wire rather than re-derived from source.
// A malformed *runtime* instance id is not caught at build time, since a provider's GetInstances() runs
// after the plugin has already started; MDC0103 is what actually earns this category its runtime keep.
// Since #522 every subject that reaches a session with a manifest (in-process and artifact; never a bare
// executable, which nothing resolves a manifest.json for) already had its manifestVersion and its id's
// agreement with configuration checked the hard way, at Build() - so MDC0105's id half is a regression
// guard for the same reason MDC0101/MDC0102 are. MDC0104's compatibility.protocol assertion is not:
// PluginHostBuilder.Build() reads a stripped-down manifest model that does not carry Compatibility at
// all, so a genuinely out-of-range protocol range only ever gets caught here.

/// <summary>Regression guard: the wire-reported plugin id is a valid <see cref="OwnerIdKind.Package" /> id.</summary>
internal sealed class PluginIdIsValidCheck() : ConformanceCheckBase("MDC0101",
	"The plugin id is a valid reverse-domain package id",
	ConformanceCategory.ManifestAndIdentifiers,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var report = await context.Plugin.ProbeHealthAsync().ConfigureAwait(false);

		if (report.Id is not { Length: > 0 } id)
		{
			return ConformanceCheckResult.Skip("The subject's /_macrodeck/info did not report a plugin id.");
		}

		return MacroDeckId.TryValidateOwnerId(id, OwnerIdKind.Package, out var error)
			? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("plugin id", id)])
			: ConformanceCheckResult.Fail(
				"The plugin id passes MacroDeckId.IsValidOwnerId(_, OwnerIdKind.Package). A build declaring an " +
				"invalid id never starts - PluginHostBuilder.Build rejects it - so this is a regression guard.",
				$"'{id}' failed validation: {error}");
	}
}

/// <summary>Regression guard: every declared capability's local id, read from the wire, is a valid <see cref="LocalIdKind.Declared" /> id.</summary>
internal sealed class DeclaredLocalIdsAreValidCheck() : ConformanceCheckBase("MDC0102",
	"Every declared capability's local id is a valid declared-kind identifier",
	ConformanceCategory.ManifestAndIdentifiers,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		foreach (var capability in context.Session!.Declared)
		{
			if (!MacroDeckId.TryValidateLocalId(capability.LocalId, LocalIdKind.Declared, out var error))
			{
				return Task.FromResult(ConformanceCheckResult.Fail(
					"Every declared local id passes MacroDeckId.TryValidateLocalId(_, LocalIdKind.Declared). A " +
					"build declaring an invalid one never starts - PluginHostBuilder.Build rejects it - so " +
					"this is a regression guard, read from the wire rather than from source.",
					$"'{capability.LocalId}' (kind '{capability.Kind}') failed validation: {error}"));
			}
		}

		return Task.FromResult(ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("declared capabilities checked", context.Session.Declared.Count)
		]));
	}
}

/// <summary>
/// The check with a real, runtime-only violator: a provider's runtime instance id (a weather station id,
/// here) is chosen after the plugin has already started, so nothing at build time can catch a malformed one.
/// </summary>
internal sealed class RuntimeInstanceIdsAreValidCheck() : ConformanceCheckBase("MDC0103",
	"Every weather station instance id is a valid resource-kind identifier",
	ConformanceCategory.ManifestAndIdentifiers,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		if (context.Session!.Declared.All(capability =>
			!string.Equals(capability.Kind, CapabilityKinds.Weather, StringComparison.Ordinal)))
		{
			return ConformanceCheckResult.Skip("This subject does not declare the weather capability.");
		}

		var outcome = await context.Session.Weather.GetInstancesAsync().ConfigureAwait(false);

		if (!outcome.Succeeded)
		{
			return ConformanceCheckResult.Fail(
				"weather/instances succeeds for a subject that declared the weather capability.",
				$"weather/instances failed: {outcome.Error?.Code} - {outcome.Error?.Message}");
		}

		var instances = outcome.DataAs<WeatherInstancesResult>()?.Instances ?? [];

		foreach (var instance in instances)
		{
			if (!MacroDeckId.TryValidateLocalId(instance.Id, LocalIdKind.Resource, out var error))
			{
				return ConformanceCheckResult.Fail(
					"Every runtime instance id passes MacroDeckId.TryValidateLocalId(_, LocalIdKind.Resource): " +
					$"non-empty, at most {MacroDeckId.MaxResourceLocalIdLength} characters, free of " +
					"QualifiedId.Separator and of whitespace or control characters.",
					$"Weather instance id '{instance.Id}' failed validation: {error}");
			}
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("weather instances checked", instances.Count)
		]);
	}
}

/// <summary>
/// Runs for every subject <see cref="ConformanceContext.Manifest" /> is non-null for - an in-process
/// subject's manifest is exactly what <see cref="ConformanceSubject.InProcess" /> was given or defaulted
/// to, and an artifact subject's has already been accepted by <c>PluginManifestReader</c>: id/version
/// already match the directory names and every entrypoint already resolved safely inside the version
/// directory, since a manifest failing any of those checks is never returned in the first place. What
/// that reader deliberately does <em>not</em> check, for an artifact, is whether <c>compatibility.protocol</c>
/// covers this suite's own protocol version (that is an installer decision, not a reader one - see
/// <c>PluginManifestReader.ValidateCompatibility</c>'s own remarks); <c>PluginHostBuilder.Build</c> never
/// checks it for an in-process subject either, since its own stripped-down manifest model does not carry
/// <c>Compatibility</c> at all. This check adds exactly that one assertion on top, for both.
/// </summary>
internal sealed class ArtifactManifestIsWellFormedCheck() : ConformanceCheckBase("MDC0104",
	"The manifest declares a supported manifest version and a protocol range this suite satisfies",
	ConformanceCategory.ManifestAndIdentifiers,
	ConformanceRequirement.Required,
	ConformancePrecondition.Manifest)
{
	public override Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var manifest = context.Manifest!;

		if (manifest.ManifestVersion != PluginManifest.SupportedManifestVersion)
		{
			return Task.FromResult(ConformanceCheckResult.Fail(
				$"manifestVersion equals {PluginManifest.SupportedManifestVersion}.",
				$"manifestVersion is {manifest.ManifestVersion}."));
		}

		if (manifest.Compatibility?.Protocol is { } protocol &&
			(protocol.Minimum > ProtocolVersions.Current || protocol.Maximum < ProtocolVersions.Minimum))
		{
			return Task.FromResult(ConformanceCheckResult.Fail(
				$"compatibility.protocol overlaps the protocol range this suite speaks ({ProtocolVersions.Minimum}-{ProtocolVersions.Current}).",
				$"compatibility.protocol declares {protocol.Minimum}-{protocol.Maximum}, which does not overlap it."));
		}

		return Task.FromResult(ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("manifestVersion", manifest.ManifestVersion),
			ConformanceCheckSupport.Observe("id", manifest.Id),
			ConformanceCheckSupport.Observe("version", manifest.Version)
		]));
	}
}

/// <summary>
/// Cross-checks the two places a subject's id lives once it has a manifest: the manifest itself, and the
/// id the subject actually operates under, read the same way <see cref="PluginIdIsValidCheck" /> (MDC0101)
/// already does. Regression guard, like MDC0101/MDC0102: <c>PluginHostBuilder.Build</c> already rejects a
/// manifest id that disagrees with a configured one before a session can exist at all (see
/// <c>PluginHostBuilder.BuildMetadata</c>'s own remarks on why - a packaging or launch mismatch the host
/// would reject anyway), so nothing that reaches a live session with a manifest could ever fail this. Kept
/// as its own assertion rather than folded into MDC0104 anyway, since "well-formed" and "agrees with what
/// is actually running" are two different claims about a manifest.
/// </summary>
internal sealed class ManifestIdMatchesReportedIdCheck() : ConformanceCheckBase("MDC0105",
	"The manifest's id equals the id the subject reports at /_macrodeck/info",
	ConformanceCategory.ManifestAndIdentifiers,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session,
	ConformancePrecondition.Manifest)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var report = await context.Plugin.ProbeHealthAsync().ConfigureAwait(false);

		if (report.Id is not { Length: > 0 } reportedId)
		{
			return ConformanceCheckResult.Skip("The subject's /_macrodeck/info did not report a plugin id.");
		}

		var manifestId = context.Manifest!.Id;

		return string.Equals(manifestId, reportedId, StringComparison.Ordinal)
			? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("id", reportedId)])
			: ConformanceCheckResult.Fail("The manifest's id equals the id reported at /_macrodeck/info.",
				$"The manifest declares '{manifestId}', but /_macrodeck/info reports '{reportedId}'.");
	}
}

/// <summary>
/// Cross-checks the manifest's <c>name</c> and <c>version</c> against what the subject actually reports at
/// <c>/_macrodeck/info</c> - the same pairing <see cref="ManifestIdMatchesReportedIdCheck" /> (MDC0105)
/// already does for <c>id</c>, extended to the other two identity fields #560 moved onto the manifest.
/// Unlike MDC0105, this is not a pure regression guard: <c>PluginHostBuilder.Build</c> validates that
/// <c>name</c> and <c>version</c> are non-empty, but nothing at build time cross-checks them against what
/// <see cref="MacroDeck.Plugin.Testing.PluginHealthReport" /> ends up reporting, so a wiring mistake
/// between reading the manifest and answering <c>/_macrodeck/info</c> is exactly what this catches.
/// </summary>
internal sealed class ManifestNameAndVersionAreReportedCheck() : ConformanceCheckBase("MDC0106",
	"The manifest's name and version equal what the subject reports at /_macrodeck/info",
	ConformanceCategory.ManifestAndIdentifiers,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session,
	ConformancePrecondition.Manifest)
{
	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var report = await context.Plugin.ProbeHealthAsync().ConfigureAwait(false);

		if (report.Name is not { Length: > 0 } reportedName || report.Version is not { Length: > 0 } reportedVersion)
		{
			return ConformanceCheckResult.Skip("The subject's /_macrodeck/info did not report a name or a version.");
		}

		var manifest = context.Manifest!;

		if (!string.Equals(manifest.Name, reportedName, StringComparison.Ordinal))
		{
			return ConformanceCheckResult.Fail("The manifest's name equals the name reported at /_macrodeck/info.",
				$"The manifest declares '{manifest.Name}', but /_macrodeck/info reports '{reportedName}'.");
		}

		if (!string.Equals(manifest.Version, reportedVersion, StringComparison.Ordinal))
		{
			return ConformanceCheckResult.Fail(
				"The manifest's version equals the version reported at /_macrodeck/info.",
				$"The manifest declares '{manifest.Version}', but /_macrodeck/info reports '{reportedVersion}'.");
		}

		return ConformanceCheckResult.Pass([
			ConformanceCheckSupport.Observe("name", reportedName),
			ConformanceCheckSupport.Observe("version", reportedVersion)
		]);
	}
}

/// <summary>
/// When the manifest declares an <c>icon</c>, the session must declare the <c>icons</c> capability and
/// <c>icons/describe</c> must report the media type the icon's extension implies.
///
/// <para>
/// Deliberately does not call <see cref="MacroDeck.Plugin.Protocol.Assets.IconMediaTypes.ForExtension" />
/// to compute the expected media type - doing so would make this check tautological against the very SDK
/// code it exists to verify: a bug in that method would make both the production code and this check agree
/// on the wrong answer. This check's own extension-to-media-type table is redundant with
/// <c>IconMediaTypes</c> on purpose, the same way a hand-checked expected value is redundant with the
/// implementation it verifies anywhere else in this suite.
/// </para>
///
/// <para>
/// An extension this table does not recognise is skipped, not failed - flagging an unsupported icon
/// extension is MDP1003's job, at compile time, not this run-time check's.
/// </para>
/// </summary>
internal sealed class ManifestIconMediaTypeIsReportedCheck() : ConformanceCheckBase("MDC0107",
	"When the manifest declares an icon, icons/describe reports the media type its extension implies",
	ConformanceCategory.ManifestAndIdentifiers,
	ConformanceRequirement.Required,
	ConformancePrecondition.Session,
	ConformancePrecondition.Manifest)
{
	// Not IconMediaTypes.ForExtension - see this type's own remarks for why. Kept private and small:
	// this is the fixed set of extensions this suite itself knows how to expect an answer for.
	private static readonly Dictionary<string, string> _expectedMediaTypeByExtension =
		new(StringComparer.OrdinalIgnoreCase)
		{
			[".svg"] = "image/svg+xml",
			[".png"] = "image/png",
			[".jpg"] = "image/jpeg",
			[".jpeg"] = "image/jpeg",
			[".webp"] = "image/webp"
		};

	public override async Task<ConformanceCheckResult> RunAsync(ConformanceContext context,
		CancellationToken cancellationToken)
	{
		var icon = context.Manifest!.Icon;

		if (string.IsNullOrEmpty(icon))
		{
			return ConformanceCheckResult.Skip("The manifest declares no icon.");
		}

		var extension = Path.GetExtension(icon);

		if (!_expectedMediaTypeByExtension.TryGetValue(extension, out var expectedMediaType))
		{
			return ConformanceCheckResult.Skip(
				$"The manifest's icon extension '{extension}' is not one this suite maps to a media type.");
		}

		if (context.Session!.Declared.All(capability =>
			!string.Equals(capability.Kind, CapabilityKinds.Icons, StringComparison.Ordinal)))
		{
			return ConformanceCheckResult.Fail(
				"The subject declares the icons capability when the manifest declares an icon.",
				"The manifest declares an icon, but the session did not declare the icons capability.");
		}

		var outcome = await context.Session.Icons.DescribeAsync().ConfigureAwait(false);

		if (!outcome.Succeeded)
		{
			return ConformanceCheckResult.Fail(
				"icons/describe succeeds for a subject that declared the icons capability.",
				$"icons/describe failed: {outcome.Error?.Code} - {outcome.Error?.Message}");
		}

		var mimeType = outcome.DataAs<IconsDescribePayload>()?.MimeType;

		return string.Equals(mimeType, expectedMediaType, StringComparison.Ordinal)
			? ConformanceCheckResult.Pass([ConformanceCheckSupport.Observe("mediaType", mimeType)])
			: ConformanceCheckResult.Fail(
				$"icons/describe reports mediaType '{expectedMediaType}' for extension '{extension}'.",
				$"icons/describe reported '{mimeType ?? "(none)"}'.");
	}
}
