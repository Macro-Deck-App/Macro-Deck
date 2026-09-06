using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Plugins.Trust;

namespace MacroDeckHost.Application.Plugins.Installation;

public enum PluginInstallWarningSeverity
{
	Advisory,
	Blocking
}

public sealed record PluginInstallWarning
{
	public required string Code { get; init; }

	public required PluginInstallWarningSeverity Severity { get; init; }

	public required string Message { get; init; }

	public string? SubjectId { get; init; }

	public static PluginInstallWarning Advisory(string code, string message, string? subjectId = null)
	{
		return new PluginInstallWarning
		{
			Code = code,
			Severity = PluginInstallWarningSeverity.Advisory,
			Message = message,
			SubjectId = subjectId
		};
	}

	public static PluginInstallWarning Blocking(string code, string message, string? subjectId = null)
	{
		return new PluginInstallWarning
		{
			Code = code,
			Severity = PluginInstallWarningSeverity.Blocking,
			Message = message,
			SubjectId = subjectId
		};
	}
}

public sealed record PluginInstallResult
{
	public required bool Success { get; init; }

	public string? PluginId { get; init; }

	public string? Version { get; init; }

	public string? PreviousVersion { get; init; }

	public bool Activated { get; init; }

	public bool RolledBack { get; init; }

	public IReadOnlyList<PluginInstallWarning> Warnings { get; init; } = [];

	public PluginInstallError? Error { get; init; }

	public string? ErrorMessage { get; init; }

	public PluginPublisher? Publisher { get; init; }

	public PluginTrustResult? Signature { get; init; }

	public PluginSignature? SignedWith { get; init; }

	public string? Name { get; init; }

	public string? Description { get; init; }

	public string? IconDataUri { get; init; }

	public bool? SupportedOnThisPlatform { get; init; }

	public bool HasBlockingWarning =>
		Warnings.Any(warning => warning.Severity == PluginInstallWarningSeverity.Blocking);

	public static PluginInstallResult Ok(string pluginId,
		string version,
		string? previousVersion,
		bool activated,
		IReadOnlyList<PluginInstallWarning>? warnings = null)
	{
		return new PluginInstallResult
		{
			Success = true,
			PluginId = pluginId,
			Version = version,
			PreviousVersion = previousVersion,
			Activated = activated,
			Warnings = warnings ?? []
		};
	}

	public static PluginInstallResult Fail(PluginInstallError error,
		string message,
		string? pluginId = null,
		string? version = null,
		string? previousVersion = null,
		bool rolledBack = false,
		IReadOnlyList<PluginInstallWarning>? warnings = null)
	{
		return new PluginInstallResult
		{
			Success = false,
			PluginId = pluginId,
			Version = version,
			PreviousVersion = previousVersion,
			RolledBack = rolledBack,
			Warnings = warnings ?? [],
			Error = error,
			ErrorMessage = message
		};
	}
}

public enum PluginArtifactSourceKind
{
	LocalPath,

	Upload,

	Url
}

/// <summary>A download-progress sample for a <see cref="PluginArtifactSourceKind.Url" /> source. Local-path
/// and upload sources never report progress: their bytes are already local, so there is nothing to wait
/// on.</summary>
public readonly record struct PluginArtifactDownloadProgress(long BytesRead, long? TotalBytes);

public sealed record PluginArtifactSource
{
	public required PluginArtifactSourceKind Kind { get; init; }

	public string? Path { get; init; }

	public Stream? Content { get; init; }

	public Uri? Url { get; init; }

	public string? ExpectedSha256 { get; init; }

	// Added after FromUrl shipped: a new required or positional parameter on FromUrl would be a binary
	// break for compiled plugins/callers, so progress is opted into with `with { Progress = ... }` instead.
	public IProgress<PluginArtifactDownloadProgress>? Progress { get; init; }

	public static PluginArtifactSource FromPath(string path)
	{
		return new PluginArtifactSource { Kind = PluginArtifactSourceKind.LocalPath, Path = path };
	}

	public static PluginArtifactSource FromUpload(Stream content)
	{
		return new PluginArtifactSource { Kind = PluginArtifactSourceKind.Upload, Content = content };
	}

	public static PluginArtifactSource FromUrl(Uri url, string? expectedSha256 = null)
	{
		return new PluginArtifactSource
		{
			Kind = PluginArtifactSourceKind.Url,
			Url = url,
			ExpectedSha256 = expectedSha256
		};
	}
}

public sealed record PluginInstallRequest
{
	public string? ExpectedPluginId { get; init; }

	public bool Force { get; init; }

	public bool RetainDownload { get; init; }

	public bool StartAfterActivation { get; init; } = true;

	// Never implied by Force: forcing a reinstall is about overwriting an existing version directory, not
	// about accepting an unsigned artifact - the two questions must stay independently answerable.
	public bool AllowUnsigned { get; init; }
}

public sealed record PluginUninstallRequest
{
	public bool KeepData { get; init; } = true;

	public bool Force { get; init; }
}

public interface IPluginInstaller
{
	Task<PluginInstallResult> Inspect(PluginArtifactSource source, CancellationToken cancellationToken = default);

	Task<PluginInstallResult> Install(PluginArtifactSource source,
		PluginInstallRequest request,
		CancellationToken cancellationToken = default);

	Task<PluginInstallResult> Activate(string pluginId,
		string version,
		CancellationToken cancellationToken = default);

	Task<PluginInstallResult> Uninstall(string pluginId,
		PluginUninstallRequest request,
		CancellationToken cancellationToken = default);
}
