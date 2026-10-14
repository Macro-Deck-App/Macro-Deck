using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Infrastructure.Portable;

internal static class PortableReadStatusMapper
{
	public static PortabilityError? ToError(PortableReadStatus status)
	{
		return status switch
		{
			PortableReadStatus.Success => null,
			PortableReadStatus.PasswordRequired => PortabilityError.PasswordRequired,
			PortableReadStatus.WrongPassword => PortabilityError.InvalidPassword,
			PortableReadStatus.UnsupportedVersion => PortabilityError.UnsupportedVersion,
			_ => PortabilityError.InvalidArchive
		};
	}
}
