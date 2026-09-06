using System.Runtime.Versioning;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore.Native;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

/// <summary>
/// A generic-password item in the login keychain. The item keeps the keychain's default access control,
/// which trusts the binary that created it: another application reading it raises an authorisation
/// prompt. That is stricter than the Windows and Linux backends, where any process running as the user
/// can read the entry, and it means an unsigned development build prompts after every rebuild - which
/// is why a development installation is normally left unprotected instead.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsKeychainKekStore : IKekStore
{
	public KeyRingBackend Backend => KeyRingBackend.MacOsKeychain;

	public KekStoreAvailability Availability => MacOsKeychainInterop.IsLoadable
		? new KekStoreAvailability(true, null)
		: new KekStoreAvailability(false, "The macOS Security framework could not be loaded");

	public KekStoreReadResult Read(KekStoreIdentity identity)
	{
		if (!MacOsKeychainInterop.IsLoadable)
		{
			return KekStoreReadResult.Unavailable(Availability.Reason!);
		}

		var handles = new List<IntPtr>();
		try
		{
			var query = BuildQuery(identity,
				handles,
				[
					MacOsKeychainInterop.SecReturnData,
					MacOsKeychainInterop.SecMatchLimit
				],
				[
					MacOsKeychainInterop.CFBooleanTrue,
					MacOsKeychainInterop.SecMatchLimitOne
				]);
			handles.Add(query);

			var status = MacOsKeychainInterop.SecItemCopyMatching(query, out var result);
			if (status == MacOsKeychainInterop.ErrSecItemNotFound)
			{
				return KekStoreReadResult.NotFound();
			}

			if (status != MacOsKeychainInterop.ErrSecSuccess)
			{
				return KekStoreReadResult.Unavailable(Describe(status));
			}

			handles.Add(result);
			var kek = MacOsKeychainInterop.CopyData(result);

			return kek is null
				? KekStoreReadResult.Unavailable("The keychain entry held no data")
				: KekStoreReadResult.Found(kek);
		}
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
		{
			return KekStoreReadResult.Unavailable(e.Message);
		}
		finally
		{
			Release(handles);
		}
	}

	public KekStoreStatus Write(KekStoreIdentity identity, ReadOnlySpan<byte> kek)
	{
		if (!MacOsKeychainInterop.IsLoadable)
		{
			return KekStoreStatus.Unavailable;
		}

		var handles = new List<IntPtr>();
		try
		{
			var value = MacOsKeychainInterop.CreateData(kek);
			handles.Add(value);

			var attributes = BuildQuery(identity, handles, [MacOsKeychainInterop.SecValueData], [value]);
			handles.Add(attributes);

			var status = MacOsKeychainInterop.SecItemAdd(attributes, IntPtr.Zero);
			if (status == MacOsKeychainInterop.ErrSecSuccess)
			{
				return KekStoreStatus.Found;
			}

			if (status != MacOsKeychainInterop.ErrSecDuplicateItem)
			{
				return KekStoreStatus.Unavailable;
			}

			var query = BuildQuery(identity, handles, [], []);
			handles.Add(query);

			var update = MacOsKeychainInterop.CreateDictionary([MacOsKeychainInterop.SecValueData], [value]);
			handles.Add(update);

			return MacOsKeychainInterop.SecItemUpdate(query, update) == MacOsKeychainInterop.ErrSecSuccess
				? KekStoreStatus.Found
				: KekStoreStatus.Unavailable;
		}
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
		{
			return KekStoreStatus.Unavailable;
		}
		finally
		{
			Release(handles);
		}
	}

	public KekStoreStatus Delete(KekStoreIdentity identity)
	{
		if (!MacOsKeychainInterop.IsLoadable)
		{
			return KekStoreStatus.Unavailable;
		}

		var handles = new List<IntPtr>();
		try
		{
			var query = BuildQuery(identity, handles, [], []);
			handles.Add(query);

			var status = MacOsKeychainInterop.SecItemDelete(query);

			return status switch
			{
				MacOsKeychainInterop.ErrSecSuccess => KekStoreStatus.Found,
				MacOsKeychainInterop.ErrSecItemNotFound => KekStoreStatus.NotFound,
				_ => KekStoreStatus.Unavailable
			};
		}
		catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
		{
			return KekStoreStatus.Unavailable;
		}
		finally
		{
			Release(handles);
		}
	}

	private static IntPtr BuildQuery(KekStoreIdentity identity,
		List<IntPtr> handles,
		IntPtr[] extraKeys,
		IntPtr[] extraValues)
	{
		var service = MacOsKeychainInterop.CreateString(identity.Service);
		var account = MacOsKeychainInterop.CreateString(identity.Account);
		handles.Add(service);
		handles.Add(account);

		IntPtr[] keys =
		[
			MacOsKeychainInterop.SecClass,
			MacOsKeychainInterop.SecAttrService,
			MacOsKeychainInterop.SecAttrAccount,
			.. extraKeys
		];
		IntPtr[] values =
		[
			MacOsKeychainInterop.SecClassGenericPassword,
			service,
			account,
			.. extraValues
		];

		return MacOsKeychainInterop.CreateDictionary(keys, values);
	}

	private static void Release(List<IntPtr> handles)
	{
		foreach (var handle in handles.Where(handle => handle != IntPtr.Zero))
		{
			MacOsKeychainInterop.CFRelease(handle);
		}
	}

	private static string Describe(int status)
		=> status switch
		{
			-25308 => "The keychain refused access without user interaction (errSecInteractionNotAllowed)",
			-25293 => "The keychain rejected the authorisation (errSecAuthFailed)",
			-128 => "The keychain prompt was cancelled (errSecUserCanceled)",
			_ => $"The keychain returned OSStatus {status}"
		};
}
