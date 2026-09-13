namespace MacroDeckHost.Application.Auth;

public interface IHostIdentityKeyProvider
{
	// The uncompressed P-256 point, 0x04 followed by X and Y: 65 bytes.
	byte[] PublicKey { get; }

	// ECDSA P-256 over SHA-256, DER encoded.
	byte[] Sign(ReadOnlySpan<byte> message);
}

public sealed class HostIdentityUnavailableException : Exception
{
	public HostIdentityUnavailableException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}
