namespace MacroDeckHost.Application.Adb;

/// <summary>Init systems a device-side service restart knows how to drive (issue #727).</summary>
public enum AdbServiceManager
{
	/// <summary>What the Car Thing's community firmwares run their kiosk browser under.</summary>
	Supervisord,
	Systemd,
	SysVInit
}
