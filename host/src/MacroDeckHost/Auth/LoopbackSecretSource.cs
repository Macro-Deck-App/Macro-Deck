using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using MacroDeckHost.Application.Configuration;

namespace MacroDeckHost.Auth;

public sealed record ResolvedLoopbackSecret(string Secret, bool Generated);

public static class LoopbackSecretSource
{
	// Plugins are spawned with this process's environment, so the value must not stay in it.
	public static ResolvedLoopbackSecret Resolve()
	{
		var provided = Environment.GetEnvironmentVariable(LoopbackSecret.EnvironmentVariable);
		Environment.SetEnvironmentVariable(LoopbackSecret.EnvironmentVariable, null);

		return LoopbackSecret.TryParse(provided, out _)
			? new ResolvedLoopbackSecret(provided!.Trim().ToLowerInvariant(), false)
			: new ResolvedLoopbackSecret(LoopbackSecret.Generate(), true);
	}

	public static string Write(string configDirectory, string secret)
	{
		Directory.CreateDirectory(configDirectory);
		var path = Path.Combine(configDirectory, LoopbackSecret.SecretFileName);
		var temporary = Path.Combine(configDirectory, $".{LoopbackSecret.SecretFileName}.{Guid.NewGuid():N}.tmp");

		try
		{
			using (var stream = CreateOwnerOnly(temporary))
			using (var writer = new StreamWriter(stream))
			{
				writer.Write(secret);
			}

			File.Move(temporary, path, overwrite: true);
		}
		finally
		{
			File.Delete(temporary);
		}

		return path;
	}

	private static FileStream CreateOwnerOnly(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			return CreateOwnerOnlyOnWindows(path);
		}

		return new FileStream(path,
			new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
			});
	}

	[SupportedOSPlatform("windows")]
	private static FileStream CreateOwnerOnlyOnWindows(string path)
	{
		var user = WindowsIdentity.GetCurrent().User!;
		var security = new FileSecurity();
		security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		security.SetOwner(user);
		security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));

		return new FileInfo(path).Create(FileMode.CreateNew,
			FileSystemRights.Write | FileSystemRights.ReadData | FileSystemRights.Synchronize,
			FileShare.None,
			4096,
			FileOptions.None,
			security);
	}
}
