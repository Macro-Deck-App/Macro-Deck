namespace MacroDeckHost.Application.Auth;

public interface IHostIdentityKeyProvider
{
	// The uncompressed P-256 point, 0x04 followed by X and Y: 65 bytes.
	ValueTask<byte[]> GetPublicKey(CancellationToken cancellationToken = default);

	// ECDSA P-256 over SHA-256, DER encoded.
	ValueTask<byte[]> Sign(byte[] message, CancellationToken cancellationToken = default);
}

public sealed class HostIdentityUnavailableException : Exception
{
	public HostIdentityUnavailableException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}
