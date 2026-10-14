namespace MacroDeckHost.Application.Portable;

public static class PortablePasswordPolicy
{
	public const int MinLength = 8;

	public static bool IsAcceptable(string? password)
	{
		return password is not null && password.Length >= MinLength;
	}
}
