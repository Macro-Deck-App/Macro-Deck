namespace MacroDeckHost.Application.ThirdParty;

public interface IThirdPartyNotices
{
	string? ReadText();

	ThirdPartyNoticesDocument? Read();
}
