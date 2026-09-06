using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Store.Operations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StoreOperationError
{
	RegistryUnavailable,
	PackageNotFound,
	PackageRemoved,
	Unsupported,
	DownloadFailed,
	ArtifactTooLarge,
	SizeMismatch,
	ChecksumMismatch,
	SignatureInvalid,
	SignatureUntrusted,
	UnsignedNotPermitted,
	TrustDowngrade,
	Incompatible,
	MalformedPackage,
	InstallFailed,
	Interrupted,
	Cancelled
}
