using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public enum KeyRingProtectionMode
{
	/// <summary>The ring is readable on disk, as it was before this feature existed.</summary>
	Unprotected,

	/// <summary>The ring is wrapped and the key encryption key is in hand.</summary>
	Protected,

	/// <summary>The ring is wrapped and the key encryption key is not available.</summary>
	Locked
}

public static class KeyRingDataProtection
{
	/// <summary>
	/// The single place both Data Protection registrations are configured - the provider Program.cs
	/// builds before the container exists, and the one Startup.cs adds - so the two cannot drift into
	/// disagreeing about how the ring on disk is protected.
	/// </summary>
	public static IDataProtectionBuilder Configure(IDataProtectionBuilder builder,
		KeyRingKekHolder holder,
		KeyRingProtectionMode mode)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// The decryptor is activated by type name out of the key file, and Data Protection's activator
		// prefers a constructor taking its own service provider - so the accessor has to live in the
		// builder's services, not the application's.
		builder.Services.AddSingleton<IKeyRingKekAccessor>(holder);

		switch (mode)
		{
			case KeyRingProtectionMode.Protected:
				builder.Services.Configure<KeyManagementOptions>(options =>
					options.XmlEncryptor = new KeyRingXmlEncryptor(holder));
				break;

			case KeyRingProtectionMode.Locked:
				// Without this, a ring whose key encryption key is missing is not an error: Data
				// Protection finds no usable key, quietly creates one, and every existing secret
				// becomes unreadable while the host looks healthy.
				builder.DisableAutomaticKeyGeneration();
				break;
		}

		return builder;
	}
}
