using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Security;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Security.KeyRing;

[TestFixture]
public class RetryKeyRingKeystoreHandlerTests
{
	private TestPaths _paths = null!;
	private FakeKekStore _store = null!;
	private RecordingRestartService _restart = null!;
	private KeyRingProtectionService _keyRing = null!;
	private byte[] _kek = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_store = new FakeKekStore();
		_restart = new RecordingRestartService();
		_kek = KeyRingKeyDerivation.CreateKek();
		_keyRing = new KeyRingProtectionService(_paths,
			_store,
			new KekStoreIdentity("Macro Deck Tests", "key-ring-kek"),
			new KeyRingKekHolder(),
			new KeyRingProtectionPlan(KeyRingProtectionMode.Locked,
				KeyRingLockReason.KeystoreUnavailable,
				KeyRingUnprotectedReason.None,
				_store.Backend,
				true,
				null,
				null,
				KeyRingKeyDerivation.DeriveKekId(_kek),
				false),
			false,
			TimeProvider.System,
			Log.Logger);
	}

	[TearDown]
	public void TearDown()
	{
		_keyRing.Dispose();
	}

	[Test]
	public async Task A_recovered_key_restarts_the_host_to_unlock()
	{
		_store.Seed(_kek);

		var response = await Retry();

		Assert.Multiple(() =>
		{
			Assert.That(response.Outcome, Is.EqualTo("Recovered"));
			Assert.That(response.RestartRequested, Is.True);
			Assert.That(_restart.Reasons, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_recovered_key_on_a_host_that_cannot_restart_itself_says_so()
	{
		_store.Seed(_kek);
		_restart.Supported = false;

		var response = await Retry();

		Assert.Multiple(() =>
		{
			Assert.That(response.Outcome, Is.EqualTo("Recovered"));
			Assert.That(response.RestartRequested, Is.False);
			Assert.That(response.RestartSupported, Is.False);
		});
	}

	[Test]
	public async Task A_keystore_that_still_cannot_answer_does_not_restart_the_host()
	{
		_store.ReadOverride = KekStoreStatus.Unavailable;

		var response = await Retry();

		Assert.Multiple(() =>
		{
			Assert.That(response.Outcome, Is.EqualTo("Unavailable"));
			Assert.That(response.RestartRequested, Is.False);
			Assert.That(_restart.Reasons, Is.Empty);
		});
	}

	private async Task<RetryKeyRingKeystoreResponse> Retry()
	{
		var handler = new RetryKeyRingKeystoreRequestMessageHandler(_keyRing, _restart);

		return await handler.Handle(new RetryKeyRingKeystoreRequest(), CancellationToken.None);
	}

	private sealed class RecordingRestartService : IApplicationRestartService
	{
		public List<string> Reasons { get; } = [];

		public bool Supported { get; set; } = true;

		public RestartAvailability Availability => new(Supported, Supported ? null : "no shell");

		public bool RestartRequested => Reasons.Count > 0;

		public Result<RestartError> Request(string reason)
		{
			if (!Supported)
			{
				return Result.Fail(RestartError.NotSupported);
			}

			Reasons.Add(reason);

			return Result.Ok<RestartError>();
		}
	}
}
