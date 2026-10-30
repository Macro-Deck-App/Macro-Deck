namespace MacroDeckHost.Application.Persistence;

public sealed class ProfilePersistenceException : Exception
{
	public ProfilePersistenceException(Guid profileId)
		: base($"Profile {profileId} could not be written; the change was not saved.")
	{
		ProfileId = profileId;
	}

	public Guid ProfileId { get; }
}
