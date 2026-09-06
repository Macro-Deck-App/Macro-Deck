using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeckHost.Application.Plugins.Installation;

public static class PluginDependencyWarningCodes
{
	public const string DependencyUnresolved = "dependency_unresolved";

	public const string DependencyVersionUnsatisfied = "dependency_version_unsatisfied";

	public const string ConflictInstalled = "conflict_installed";

	public const string IconPackUnresolved = "iconpack_unresolved";

	public const string UnknownPermission = "unknown_permission";

	public const string PermissionsRequested = "permissions_requested";

	public const string NoFileDigests = "no_file_digests";

	public const string SignatureUnverified = "signature_unverified";

	public const string ArtifactUnsigned = "unsigned";

	/// <summary>Blocking, and only ever attached to an <see cref="IPluginInstaller.Inspect" /> preview -
	/// see the remarks on that method for why it never gates an actual install.</summary>
	public const string NotTrusted = "not_trusted";

	public const string SdkCompatibilityUnchecked = "sdk_compatibility_unchecked";

	public const string NoEntrypointForRuntime = "no_entrypoint_for_runtime";
}

public interface IPluginDependencyResolver
{
	IReadOnlyList<PluginInstallWarning> Resolve(PluginManifest manifest);

	IReadOnlyList<string> FindHardDependents(string pluginId);
}
