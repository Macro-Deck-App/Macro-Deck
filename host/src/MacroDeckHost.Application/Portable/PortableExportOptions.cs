using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Portable;

public sealed record PortableExportOptions
{
	public static readonly PortableExportOptions Default = new();

	public bool IncludeIcons { get; init; } = true;

	public bool IncludeSecrets { get; init; }

	public bool IncludeSubfolders { get; init; }

	public string? Password { get; init; }

	public PortabilityError? Validate()
	{
		if (IncludeSecrets && string.IsNullOrEmpty(Password))
		{
			return PortabilityError.PasswordRequired;
		}

		if (!string.IsNullOrEmpty(Password) && !PortablePasswordPolicy.IsAcceptable(Password))
		{
			return PortabilityError.WeakPassword;
		}

		return null;
	}
}
