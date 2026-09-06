using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore.Native;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

/// <summary>
/// Credential Manager keyed by target name, with the stored blob additionally DPAPI-protected. The
/// credential store on its own hands the blob back to any process running as the user, so it is the
/// DPAPI layer that makes an exported credential dump useless on another machine or account.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialManagerKekStore : IKekStore
{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MacroDeck.KeyRing.v1");

	public KeyRingBackend Backend => KeyRingBackend.WindowsCredentialManager;

	public KekStoreAvailability Availability => new(true, null);

	public KekStoreReadResult Read(KekStoreIdentity identity)
	{
		if (!WindowsCredentialManagerInterop.CredRead(TargetName(identity),
			WindowsCredentialManagerInterop.CredTypeGeneric,
			0,
			out var handle))
		{
			var error = Marshal.GetLastWin32Error();

			return error == WindowsCredentialManagerInterop.ErrorNotFound
				? KekStoreReadResult.NotFound()
				: KekStoreReadResult.Unavailable(new Win32Exception(error).Message);
		}

		try
		{
			var credential = Marshal.PtrToStructure<WindowsCredentialManagerInterop.Credential>(handle);
			if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
			{
				return KekStoreReadResult.Unavailable("The stored credential held no data");
			}

			var protectedKek = new byte[credential.CredentialBlobSize];
			Marshal.Copy(credential.CredentialBlob, protectedKek, 0, protectedKek.Length);

			try
			{
				return KekStoreReadResult.Found(ProtectedData.Unprotect(protectedKek,
					Entropy,
					DataProtectionScope.CurrentUser));
			}
			catch (CryptographicException e)
			{
				// The entry exists but this user or machine cannot open it. Reporting that as "no
				// entry" would mint a replacement key and strand every secret the original protects.
				return KekStoreReadResult.Unavailable($"The stored credential could not be unprotected: {e.Message}");
			}
		}
		finally
		{
			WindowsCredentialManagerInterop.CredFree(handle);
		}
	}

	public KekStoreStatus Write(KekStoreIdentity identity, ReadOnlySpan<byte> kek)
	{
		byte[] protectedKek;
		try
		{
			protectedKek = ProtectedData.Protect(kek.ToArray(), Entropy, DataProtectionScope.CurrentUser);
		}
		catch (CryptographicException)
		{
			return KekStoreStatus.Unavailable;
		}

		var target = Marshal.StringToCoTaskMemUni(TargetName(identity));
		var account = Marshal.StringToCoTaskMemUni(identity.Account);
		var blob = Marshal.AllocCoTaskMem(protectedKek.Length);
		try
		{
			Marshal.Copy(protectedKek, 0, blob, protectedKek.Length);

			var credential = new WindowsCredentialManagerInterop.Credential
			{
				Type = WindowsCredentialManagerInterop.CredTypeGeneric,
				TargetName = target,
				CredentialBlobSize = (uint)protectedKek.Length,
				CredentialBlob = blob,
				Persist = WindowsCredentialManagerInterop.CredPersistLocalMachine,
				UserName = account
			};

			return WindowsCredentialManagerInterop.CredWrite(ref credential, 0)
				? KekStoreStatus.Found
				: KekStoreStatus.Unavailable;
		}
		finally
		{
			Marshal.FreeCoTaskMem(target);
			Marshal.FreeCoTaskMem(account);
			Marshal.FreeCoTaskMem(blob);
			CryptographicOperations.ZeroMemory(protectedKek);
		}
	}

	public KekStoreStatus Delete(KekStoreIdentity identity)
	{
		if (WindowsCredentialManagerInterop.CredDelete(TargetName(identity),
			WindowsCredentialManagerInterop.CredTypeGeneric,
			0))
		{
			return KekStoreStatus.Found;
		}

		return Marshal.GetLastWin32Error() == WindowsCredentialManagerInterop.ErrorNotFound
			? KekStoreStatus.NotFound
			: KekStoreStatus.Unavailable;
	}

	private static string TargetName(KekStoreIdentity identity) => $"{identity.Service}:{identity.Account}";
}
