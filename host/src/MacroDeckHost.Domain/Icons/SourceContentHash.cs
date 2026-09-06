namespace MacroDeckHost.Domain.Icons;

public readonly record struct SourceContentHash
{
	private SourceContentHash(string value) => Value = value;

	public string Value { get; }

	public static SourceContentHash Compute(ReadOnlySpan<byte> content) => new(ContentHash.Compute(content));

	public static SourceContentHash FromComputed(string value) => new(value);

	public static bool TryParse(string? value, out SourceContentHash hash)
	{
		var normalized = ContentHash.Normalize(value);
		hash = normalized is null ? default : new SourceContentHash(normalized);
		return normalized is not null;
	}

	public override string ToString() => Value;
}
