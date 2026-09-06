namespace MacroDeck.Signing.Packages;

/// <summary>The outcome of computing a package's canonical signature digest.</summary>
public sealed record PackageDigestResult
{
	public required bool Success { get; init; }

	public byte[]? Digest { get; init; }

	public SigningError? Error { get; init; }

	public string? Message { get; init; }

	public static PackageDigestResult Ok(byte[] digest) => new() { Success = true, Digest = digest };

	public static PackageDigestResult Fail(SigningError error, string message) =>
		new() { Success = false, Error = error, Message = message };
}
