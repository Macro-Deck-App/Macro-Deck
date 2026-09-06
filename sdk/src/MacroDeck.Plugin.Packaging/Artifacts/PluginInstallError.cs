namespace MacroDeck.Plugin.Packaging.Artifacts;

/// <summary>Why an install, activation or uninstall could not complete.</summary>
public enum PluginInstallError
{
	ArtifactNotFound,
	ArtifactTooLarge,

	/// <summary>Not a readable ZIP.</summary>
	InvalidArchive,

	/// <summary>Traversal, absolute path, symlink, reserved name or duplicate entry.</summary>
	UnsafeEntry,

	/// <summary>Entry count, byte total or compression ratio exceeded.</summary>
	ArtifactLimitExceeded,

	ManifestMissing,
	ManifestInvalid,

	/// <summary>The manifest names a different plugin than the caller asked to install.</summary>
	IdMismatch,

	/// <summary>Declared protocol or Macro Deck compatibility excludes this host.</summary>
	Incompatible,

	/// <summary>A declared file digest, or the artifact's expected hash, did not match.</summary>
	HashMismatch,

	/// <summary>The signature block is malformed, or does not verify against the signed bytes.</summary>
	SignatureInvalid,

	AlreadyInstalled,
	StagingFailed,
	ActivationFailed,

	/// <summary>The activated version never reached a healthy running state; the install was rolled back.
	/// </summary>
	HealthValidationFailed,

	/// <summary>Another installed plugin hard-depends on the one being uninstalled.</summary>
	DependencyInUse,

	NotInstalled,
	Cancelled,
	Failed,

	/// <summary>The certificate chain does not lead to a trusted root, or the certificate does not permit
	/// the required usage or was not valid at the time of signing.</summary>
	SignatureUntrusted,

	/// <summary>The signing certificate has been revoked.</summary>
	SignatureRevoked,

	/// <summary>The signature could not be verified by this host, e.g. an unreadable package or an
	/// unsupported algorithm.</summary>
	SignatureUnverifiable,

	/// <summary>The artifact is unsigned and the request did not grant consent to install it unsigned, or
	/// its source kind never permits unsigned installs.</summary>
	UnsignedNotPermitted,

	/// <summary>An installed plugin was admitted as trusted; this update would install at a lower trust
	/// tier, which is refused even with consent.</summary>
	TrustDowngrade
}
