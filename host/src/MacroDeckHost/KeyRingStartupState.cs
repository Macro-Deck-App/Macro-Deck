using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

namespace MacroDeckHost;

/// <summary>
/// How the key ring was found at startup, established before the dependency injection container exists
/// because the Data Protection provider is built before it too. <see cref="Startup"/> constructs its own
/// <c>MacroDeckPaths</c> and has no other handle on what Program.cs decided.
/// </summary>
public static class KeyRingStartupState
{
	private static KekStoreIdentity? _identity;

	public static KeyRingProtectionPlan Plan { get; private set; } = Unresolved();

	public static KeyRingKekHolder KekHolder { get; private set; } = new();

	public static IKekStore Store { get; private set; } = new NullKekStore("The key ring state was never resolved");

	public static KekStoreIdentity Identity => _identity ??= new KekStoreIdentity("Macro Deck", "key-ring-kek");

	public static bool Portable { get; private set; }

	/// <summary>
	/// Where a locked host points Data Protection instead of the real key ring, so that nothing it does
	/// can create, rotate or overwrite a key file it cannot read.
	/// </summary>
	public static string? ScratchKeysDirectory { get; private set; }

	public static bool IsLocked => Plan.Mode == KeyRingProtectionMode.Locked;

	public static void Set(KeyRingProtectionPlan plan,
		KeyRingKekHolder holder,
		IKekStore store,
		KekStoreIdentity identity,
		bool portable,
		string? scratchKeysDirectory)
	{
		Plan = plan;
		KekHolder = holder;
		Store = store;
		_identity = identity;
		Portable = portable;
		ScratchKeysDirectory = scratchKeysDirectory;
	}


	private static KeyRingProtectionPlan Unresolved()
		=> new(KeyRingProtectionMode.Unprotected,
			KeyRingLockReason.None,
			KeyRingUnprotectedReason.None,
			KeyRingBackend.None,
			false,
			null,
			null,
			null,
			false);
}
