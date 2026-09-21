using System.Text.RegularExpressions;
using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Application.Store;

public enum StoreDownloadOperation
{
	Install,
	Update,
	Repair
}

public sealed partial record StoreDownloadMetadata
{
	public const string AssetHost = "store-assets.macro-deck.app";
	public const string OperationHeader = "X-MacroDeck-Operation";
	public const string OperationIdHeader = "X-MacroDeck-Operation-Id";
	public const string CurrentVersionHeader = "X-MacroDeck-Current-Version";

	public required StoreDownloadOperation Operation { get; init; }

	public required Guid OperationId { get; init; }

	public string? CurrentVersion { get; init; }

	public static StoreDownloadMetadata? For(StoreOperation operation, string targetVersion)
	{
		ArgumentNullException.ThrowIfNull(operation);

		if (operation.Kind == StoreOperationKind.TestInstall)
		{
			return null;
		}

		var operationId = operation.RootOperationId ?? operation.Id;
		if (operation.PreviousVersion is not { } installed)
		{
			return new StoreDownloadMetadata { Operation = StoreDownloadOperation.Install, OperationId = operationId };
		}

		return new StoreDownloadMetadata
		{
			Operation = SameVersion(installed, targetVersion)
				? StoreDownloadOperation.Repair
				: StoreDownloadOperation.Update,
			OperationId = operationId,
			CurrentVersion = installed
		};
	}

	// The operation id and installed version are only disclosed to the Store's own asset host. Redirects are
	// not re-checked here: both artifact clients rely on AllowAutoRedirect being off.
	public void ApplyTo(HttpRequestMessage request)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.RequestUri is not { IsAbsoluteUri: true } uri ||
			!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
			!string.Equals(uri.Host, AssetHost, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		request.Headers.TryAddWithoutValidation(OperationHeader, WireName(Operation));
		request.Headers.TryAddWithoutValidation(OperationIdHeader, OperationId.ToString("D"));

		if (Operation != StoreDownloadOperation.Install &&
			CurrentVersion is { } version &&
			AcceptedVersion().IsMatch(version))
		{
			request.Headers.TryAddWithoutValidation(CurrentVersionHeader, version);
		}
	}

	private static bool SameVersion(string installed, string target) =>
		SemanticVersion.TryParse(installed, out var installedVersion) &&
		SemanticVersion.TryParse(target, out var targetVersion)
			? installedVersion.Equals(targetVersion)
			: string.Equals(installed, target, StringComparison.OrdinalIgnoreCase);

	private static string WireName(StoreDownloadOperation operation) => operation switch
	{
		StoreDownloadOperation.Install => "install",
		StoreDownloadOperation.Update => "update",
		StoreDownloadOperation.Repair => "repair",
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	};

	// Mirrors the download Worker's version rule, so a local version it would reject, or one that cannot
	// travel in a header at all, is left out rather than failing the download.
	[GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+_-]{0,127}$")]
	private static partial Regex AcceptedVersion();
}
