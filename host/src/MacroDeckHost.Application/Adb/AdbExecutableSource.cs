namespace MacroDeckHost.Application.Adb;

public enum AdbExecutableSource
{
	None,
	Configured,
	AndroidSdkEnvironment,
	WellKnownSdkLocation,
	Path
}
