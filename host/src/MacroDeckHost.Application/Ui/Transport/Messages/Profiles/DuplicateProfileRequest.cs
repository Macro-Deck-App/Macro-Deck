namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class DuplicateProfileRequest
{
	public string Id { get; set; } = string.Empty;
	public string? Name { get; set; }
}
