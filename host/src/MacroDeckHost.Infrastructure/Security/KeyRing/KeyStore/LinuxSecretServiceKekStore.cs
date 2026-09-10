using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Native;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore.Native;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

/// <summary>
/// Secret Service through libsecret, which is absent on a headless server or in a container. That is
/// the documented fallback rather than a failure: the store reports itself unsupported and the key ring
/// stays as it was.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretServiceKekStore : IKekStore
{
	public KeyRingBackend Backend => KeyRingBackend.LinuxSecretService;

	public KekStoreAvailability Availability => LinuxSecretServiceInterop.IsLoadable
		? new KekStoreAvailability(true, null)
		: new KekStoreAvailability(false, "libsecret is not installed on this system");

	public KekStoreReadResult Read(KekStoreIdentity identity)
	{
		if (!LinuxSecretServiceInterop.IsLoadable)
		{
			return KekStoreReadResult.Unavailable(Availability.Reason!);
		}

		using var attributes = LinuxSecretServiceInterop.CreateAttributes(Account(identity));
		if (!attributes.IsValid)
		{
			return KekStoreReadResult.Unavailable("The libsecret attribute table could not be created");
		}

		try
		{
			var password = LinuxSecretServiceInterop.LookupSync(LinuxSecretServiceInterop.Schema,
				attributes.Table,
				IntPtr.Zero,
				out var error);

			// A null result with no GError means the entry simply is not there; a null result *with*
			// one means the keyring could not answer - a missing D-Bus session, a locked collection -
			// and nothing may be rewritten on the strength of it.
			var message = LinuxSecretServiceInterop.TakeError(error);
			if (password == IntPtr.Zero)
			{
				return message is null
					? KekStoreReadResult.NotFound()
					: KekStoreReadResult.Unavailable(message);
			}

			try
			{
				var encoded = Marshal.PtrToStringUTF8(password);

				return string.IsNullOrEmpty(encoded)
					? KekStoreReadResult.Unavailable("The stored secret held no data")
					: KekStoreReadResult.Found(Convert.FromBase64String(encoded));
			}
			catch (FormatException)
			{
				return KekStoreReadResult.Unavailable("The stored secret was not valid base64");
			}
			finally
			{
				LinuxSecretServiceInterop.PasswordFree(password);
			}
		}
		catch (DllNotFoundException e)
		{
			return KekStoreReadResult.Unavailable(e.Message);
		}
		catch (EntryPointNotFoundException e)
		{
			return KekStoreReadResult.Unavailable(e.Message);
		}
	}

	public KekStoreStatus Write(KekStoreIdentity identity, ReadOnlySpan<byte> kek)
	{
		if (!LinuxSecretServiceInterop.IsLoadable)
		{
			return KekStoreStatus.Unavailable;
		}

		using var attributes = LinuxSecretServiceInterop.CreateAttributes(Account(identity));
		if (!attributes.IsValid)
		{
			return KekStoreStatus.Unavailable;
		}

		var encoded = Convert.ToBase64String(kek);
		var label = NativeUtf8.Alloc($"{identity.Service} key ring");
		var password = NativeUtf8.Alloc(encoded);
		try
		{
			var stored = LinuxSecretServiceInterop.StoreSync(LinuxSecretServiceInterop.Schema,
				attributes.Table,
				IntPtr.Zero,
				label,
				password,
				IntPtr.Zero,
				out var error);

			LinuxSecretServiceInterop.TakeError(error);

			return stored ? KekStoreStatus.Found : KekStoreStatus.Unavailable;
		}
		catch (DllNotFoundException)
		{
			return KekStoreStatus.Unavailable;
		}
		catch (EntryPointNotFoundException)
		{
			return KekStoreStatus.Unavailable;
		}
		finally
		{
			Marshal.FreeHGlobal(label);

			// The unmanaged copy is the one worth wiping; the managed base64 string is immutable and
			// only the garbage collector can reclaim it.
			for (var offset = 0; offset <= encoded.Length; offset++)
			{
				Marshal.WriteByte(password, offset, 0);
			}

			Marshal.FreeHGlobal(password);
		}
	}

	public KekStoreStatus Delete(KekStoreIdentity identity)
	{
		if (!LinuxSecretServiceInterop.IsLoadable)
		{
			return KekStoreStatus.Unavailable;
		}

		using var attributes = LinuxSecretServiceInterop.CreateAttributes(Account(identity));
		if (!attributes.IsValid)
		{
			return KekStoreStatus.Unavailable;
		}

		try
		{
			var cleared = LinuxSecretServiceInterop.ClearSync(LinuxSecretServiceInterop.Schema,
				attributes.Table,
				IntPtr.Zero,
				out var error);

			var message = LinuxSecretServiceInterop.TakeError(error);
			if (message is not null)
			{
				return KekStoreStatus.Unavailable;
			}

			return cleared ? KekStoreStatus.Found : KekStoreStatus.NotFound;
		}
		catch (DllNotFoundException)
		{
			return KekStoreStatus.Unavailable;
		}
		catch (EntryPointNotFoundException)
		{
			return KekStoreStatus.Unavailable;
		}
	}

	// The Secret Service collection is shared with every other application, so the service name has to
	// be part of the attribute rather than a separate field libsecret does not index on.
	private static string Account(KekStoreIdentity identity) => $"{identity.Service}:{identity.Account}";
}
