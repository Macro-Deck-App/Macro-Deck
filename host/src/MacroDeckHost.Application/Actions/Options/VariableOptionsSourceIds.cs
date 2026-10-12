using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Actions.Options;

public static class VariableOptionsSourceIds
{
	public const string Variables = HostOptionsSources.Variables;

	public const string UserVariables = HostOptionsSources.UserVariables;
}

public static class MusicPlayerOptionsSourceIds
{
	public const string Instances = "macrodeck.music-player-instances";
}

public static class DeckOptionsSourceIds
{
	public const string Profiles = "macrodeck.profiles";

	public const string Folders = "macrodeck.folders";

	public const string Integrations = "macrodeck.integrations";

	public const string Devices = HostOptionsSources.Devices;
}

public static class AdbOptionsSourceIds
{
	public const string Devices = HostOptionsSources.AndroidDevices;
}

public static class IconOptionsSourceIds
{
	public const string Appearances = "macrodeck.icon-appearances";
}
