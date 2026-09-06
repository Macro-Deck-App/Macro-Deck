using System.Security.Cryptography;

namespace MacroDeckHost.Domain.Icons;

public sealed class IncrementalContentHash : IDisposable
{
	private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

	public void Append(ReadOnlySpan<byte> content) => _hash.AppendData(content);

	public string Finish() => ContentHash.Format(_hash.GetHashAndReset());

	public void Dispose() => _hash.Dispose();
}
