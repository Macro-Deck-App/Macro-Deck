namespace MacroDeckHost.Application.Ui.Transport.Messages.Security;

public sealed class GetKeyRingStatusRequest;

/// <summary>
/// Served anonymously and on both listeners, because while the ring is locked the token signing key is
/// unreadable and a gated endpoint would be unreachable exactly when it is needed. It therefore carries
/// nothing beyond what a client needs to render the gate.
/// </summary>
public sealed class GetKeyRingStatusResponse
{
	public bool Locked { get; set; }

	public string LockReason { get; set; } = string.Empty;

	public bool RestartSupported { get; set; }

	public string? RestartUnsupportedReason { get; set; }
}

public sealed class UnlockKeyRingRequest
{
	public string RecoveryKey { get; set; } = string.Empty;
}

public sealed class UnlockKeyRingResponse
{
	public bool Success { get; set; }

	public TransportError? Error { get; set; }

	public bool RestartRequested { get; set; }

	public bool RestartSupported { get; set; }

	public string? RestartUnsupportedReason { get; set; }
}

public sealed class GetKeyRingProtectionRequest;

public sealed class GetKeyRingProtectionResponse
{
	public string State { get; set; } = string.Empty;

	public string LockReason { get; set; } = string.Empty;

	public string UnprotectedReason { get; set; } = string.Empty;

	public string Backend { get; set; } = string.Empty;

	public bool BackendAvailable { get; set; }

	/// <summary>
	/// Why the backend could not answer, in English, for logs and support. Deliberately not rendered:
	/// it is text Macro Deck authors rather than platform output, so showing it would put an
	/// untranslated sentence in front of a user reading the app in another language.
	/// </summary>
	public string? BackendUnavailableReason { get; set; }

	public string? KekId { get; set; }

	public int EscrowWrapCount { get; set; }

	public bool RecoveryKeyExported { get; set; }

	public bool MigrationPending { get; set; }
}
