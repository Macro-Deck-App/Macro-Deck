namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

public static class KekStoreFactory
{
	public static IKekStore Create()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsCredentialManagerKekStore();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOsKeychainKekStore();
		}

		if (OperatingSystem.IsLinux())
		{
			return new LinuxSecretServiceKekStore();
		}

		return new NullKekStore("This platform offers no secret storage Macro Deck can use");
	}
}
