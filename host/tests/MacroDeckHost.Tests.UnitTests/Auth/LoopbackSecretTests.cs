using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Auth;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[NonParallelizable]
public class LoopbackSecretTests
{
	private const string Nonce = "00112233445566778899aabbccddeeff";
	private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_900_000_000);

	// The same vector is asserted by the bootstrapper (ui/bootstrapper/src/loopback_secret.rs) and the
	// E2E code minter, which produce these values without the host.
	[Test]
	public void The_shared_vector_matches_every_implementation()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LoopbackSecret.SessionCookieValue(),
				Is.EqualTo("7e665b82182292969e91cbae53bca7d3f716abd058a9f70ea4090d84b0fff3f1"));
			Assert.That(LoopbackSecret.Proof(Nonce),
				Is.EqualTo("964b8fdb3ce49d81ed0660ae293e76bf10eec16fe22020daa3cc5357363a15bb"));
			Assert.That(LoopbackSecret.TryRedeemCode(
					"1900000060.00112233445566778899aabbccddeeff.c8ba672b12783beb854f70711c5a5a6df1e45a53ef4abec3163c636c932feb73",
					Now),
				Is.True);
		});
	}

	[Test]
	public void A_code_is_redeemable_once_and_only_before_it_expires()
	{
		var code = TestListenerPorts.SessionCode(TestListenerPorts.LoopbackSecret, Now, "ffeeddccbbaa99887766554433221100");
		var late = TestListenerPorts.SessionCode(TestListenerPorts.LoopbackSecret, Now, "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f");

		Assert.Multiple(() =>
		{
			Assert.That(LoopbackSecret.TryRedeemCode(late, Now + LoopbackSecret.CodeLifetime), Is.False);
			Assert.That(LoopbackSecret.TryRedeemCode(code, Now + TimeSpan.FromSeconds(1)), Is.True);
			Assert.That(LoopbackSecret.TryRedeemCode(code, Now + TimeSpan.FromSeconds(2)), Is.False);
		});
	}

	[Test]
	public void A_code_claiming_a_far_future_expiry_is_refused()
	{
		var future = TestListenerPorts.SessionCode(TestListenerPorts.LoopbackSecret,
			Now + TimeSpan.FromHours(1),
			"abababababababababababababababab");

		Assert.That(LoopbackSecret.TryRedeemCode(future, Now), Is.False);
	}

	[Test]
	public void A_proof_is_only_given_for_a_well_formed_nonce()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LoopbackSecret.Proof("session"), Is.Null);
			Assert.That(LoopbackSecret.Proof(Nonce.ToUpperInvariant()), Is.Null);
			Assert.That(LoopbackSecret.Proof(Nonce + "00"), Is.Null);
		});
	}

	[Test]
	public void A_handed_over_secret_is_used_and_removed_from_the_environment()
	{
		var previous = Environment.GetEnvironmentVariable(LoopbackSecret.EnvironmentVariable);
		try
		{
			Environment.SetEnvironmentVariable(LoopbackSecret.EnvironmentVariable, TestListenerPorts.LoopbackSecret);

			var resolved = LoopbackSecretSource.Resolve();

			Assert.Multiple(() =>
			{
				Assert.That(resolved, Is.EqualTo(new ResolvedLoopbackSecret(TestListenerPorts.LoopbackSecret, false)));
				Assert.That(Environment.GetEnvironmentVariable(LoopbackSecret.EnvironmentVariable), Is.Null);
			});
		}
		finally
		{
			Environment.SetEnvironmentVariable(LoopbackSecret.EnvironmentVariable, previous);
		}
	}

	[Test]
	public void Without_a_handed_over_secret_a_fresh_one_is_generated()
	{
		var previous = Environment.GetEnvironmentVariable(LoopbackSecret.EnvironmentVariable);
		try
		{
			Environment.SetEnvironmentVariable(LoopbackSecret.EnvironmentVariable, "too-short");

			var first = LoopbackSecretSource.Resolve();
			var second = LoopbackSecretSource.Resolve();

			Assert.Multiple(() =>
			{
				Assert.That(first.Generated, Is.True);
				Assert.That(LoopbackSecret.TryParse(first.Secret, out var bytes) && bytes.Length == 32, Is.True);
				Assert.That(second.Secret, Is.Not.EqualTo(first.Secret));
			});
		}
		finally
		{
			Environment.SetEnvironmentVariable(LoopbackSecret.EnvironmentVariable, previous);
		}
	}

	[Test]
	public void The_secret_file_is_readable_by_its_owner_only_even_when_it_replaces_an_open_one()
	{
		var directory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(directory);
			var existing = Path.Combine(directory, LoopbackSecret.SecretFileName);
			File.WriteAllText(existing, "stale");
			if (!OperatingSystem.IsWindows())
			{
				File.SetUnixFileMode(existing, (UnixFileMode)0b110_100_100);
			}

			var path = LoopbackSecretSource.Write(directory, TestListenerPorts.LoopbackSecret);

			Assert.That(File.ReadAllText(path), Is.EqualTo(TestListenerPorts.LoopbackSecret));
			if (!OperatingSystem.IsWindows())
			{
				Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
			}

			Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}
