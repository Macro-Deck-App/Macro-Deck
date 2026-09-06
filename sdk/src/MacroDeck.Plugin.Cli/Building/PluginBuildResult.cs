using MacroDeck.Plugin.Cli.Packing;

namespace MacroDeck.Plugin.Cli.Building;

internal enum PluginBuildFailureReason
{
	SourceNotFound,

	ManifestNotFound,

	/// <summary>The manifest exists but is not parseable JSON. A verdict about the subject, not an
	/// environment problem - see <see cref="PluginBuildFailureExitCode" />.</summary>
	ManifestMalformed,

	/// <summary>The manifest parses but violates the published schema. Checked before anything is built, so
	/// a manifest that can never pack does not first cost a multi-platform publish.</summary>
	ManifestInvalid,

	NoEntrypointsDeclared,

	/// <summary>The artifact already exists and <c>--force</c> was not given. Checked before building for
	/// the same reason.</summary>
	OutputExists,

	BuildConfigNotFound,

	BuildConfigMalformed,

	/// <summary>The build config parsed but says something this tool cannot act on.</summary>
	BuildConfigInvalid,

	/// <summary><c>--rid</c> named a runtime identifier the manifest does not declare.</summary>
	RidNotDeclared,

	/// <summary>A selected runtime identifier has no <c>targets</c> entry.</summary>
	TargetNotConfigured,

	/// <summary>Two selected runtime identifiers would stage into the same directory, or an entrypoint sits
	/// at the package root where per-RID outputs cannot be kept apart.</summary>
	EntrypointLayoutInvalid,

	BuildToolNotFound,

	BuildFailed,

	/// <summary>The tool succeeded but its declared <c>output</c> directory is absent or empty.</summary>
	TargetOutputMissing,

	/// <summary>A requested runtime identifier did not produce the entrypoint its manifest declares. Where
	/// <c>pack</c> only warns, a full build fails - see issue #617.</summary>
	EntrypointMissing,

	StagingFailed
}

/// <summary>What <see cref="PluginBuilder.BuildAsync" /> produced.</summary>
internal sealed record PluginBuildResult
{
	public required bool Success { get; init; }

	public IReadOnlyList<string> BuiltRids { get; init; } = [];

	public IReadOnlyList<CliDiagnostic> Warnings { get; init; } = [];

	/// <summary>The pack phase's own result, present whenever the build phase reached it. Reported through
	/// <see cref="PluginPackReporter" /> verbatim so <c>build</c> never re-derives <c>pack</c>'s codes.</summary>
	public PluginPackResult? Pack { get; init; }

	public PluginBuildFailureReason? FailureReason { get; init; }

	public string? FailureMessage { get; init; }

	/// <summary>The failing build tool's captured stdout and stderr, rendered as the diagnostic's detail.</summary>
	public string? FailureDetail { get; init; }

	public static PluginBuildResult Packed(IReadOnlyList<string> builtRids,
		PluginPackResult pack,
		IReadOnlyList<CliDiagnostic>? warnings = null)
	{
		return new PluginBuildResult
		{
			Success = pack.Success,
			BuiltRids = builtRids,
			Pack = pack,
			Warnings = warnings ?? []
		};
	}

	public static PluginBuildResult Fail(PluginBuildFailureReason reason,
		string message,
		string? detail = null,
		IReadOnlyList<CliDiagnostic>? warnings = null)
	{
		return new PluginBuildResult
		{
			Success = false,
			FailureReason = reason,
			FailureMessage = message,
			FailureDetail = detail,
			Warnings = warnings ?? []
		};
	}
}
