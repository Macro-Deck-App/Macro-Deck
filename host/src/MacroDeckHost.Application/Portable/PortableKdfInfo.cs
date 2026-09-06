namespace MacroDeckHost.Application.Portable;

public sealed class PortableKdfInfo
{
	public const string Pbkdf2HmacSha256 = "PBKDF2-HMAC-SHA256";

	public string? Id { get; set; } = Pbkdf2HmacSha256;

	public int Iterations { get; set; }

	public string? Salt { get; set; }
}
