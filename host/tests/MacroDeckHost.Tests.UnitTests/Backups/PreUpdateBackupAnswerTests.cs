using System.Reflection;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Backups;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Backups;

public class PreUpdateBackupAnswerTests
{
	private static T StubOf<T>(Func<string, object?> answer)
		where T : class
	{
		var stub = DispatchProxy.Create<T, Stub<T>>();
		((Stub<T>)(object)stub).Answer(answer);

		return stub;
	}

	public class Stub<T> : DispatchProxy
		where T : class
	{
		private Func<string, object?> _answer = null!;

		public void Answer(Func<string, object?> answer)
		{
			_answer = answer;
		}

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			return _answer(targetMethod!.Name);
		}
	}

	private static TriggerBeforeHostUpdateBackupRequestMessageHandler Handler(
		PreUpdateBackupOutcome outcome,
		string culture)
	{
		var coordinator = StubOf<IPreUpdateBackupCoordinator>(_ => Task.FromResult(outcome));

		return new TriggerBeforeHostUpdateBackupRequestMessageHandler(coordinator,
			new FakeLocalizationPreferences { Culture = culture },
			TestLocalization.Resolver);
	}

	[TestCase("en")]
	[TestCase("de")]
	public async Task A_failure_reaches_the_bootstrapper_as_the_sentence_of_the_hosts_language(string culture)
	{
		var outcome = new PreUpdateBackupOutcome(false, false, "No space left on device : /secret/path", null)
		{
			Error = BackupError.InsufficientDiskSpace
		};

		var response = await Handler(outcome, culture).Handle(new TriggerBeforeHostUpdateBackupRequest(), default);

		Assert.That(response.Reason,
			Is.EqualTo(TestLocalization.Resolver.Resolve(AppStrings.Errors.Backup.InsufficientDiskSpace(), culture)));
	}

	[Test]
	public async Task A_skip_keeps_the_reason_the_bootstrapper_recognises()
	{
		var outcome = new PreUpdateBackupOutcome(true, true, "KeyRingLocked", null);

		var response = await Handler(outcome, "de").Handle(new TriggerBeforeHostUpdateBackupRequest(), default);

		Assert.That(response.Reason, Is.EqualTo("KeyRingLocked"));
	}

	private static async Task<PreUpdateBackupOutcome> SkipOnALockedHost(Func<Task<BackupSettings>> settings)
	{
		var status = new KeyRingProtectionStatus(KeyRingProtectionState.Locked,
			default,
			default,
			default,
			false,
			null,
			null,
			0,
			false,
			false);

		var services = new ServiceCollection();
		services.AddSingleton(StubOf<IKeyRingProtectionService>(_ => status));
		services.AddSingleton(StubOf<IAppPreferenceService>(_ => settings()));
		await using var provider = services.BuildServiceProvider();

		return await new PreUpdateBackupCoordinator(provider.GetRequiredService<IServiceScopeFactory>())
			.CreateBeforeHostUpdate("3.0.2");
	}

	private static Task<BackupSettings> Backups(bool beforeHostUpdate)
	{
		return Task.FromResult(new BackupSettings("daily",
			"03:00",
			DayOfWeek.Monday,
			1,
			"keepLatest",
			5,
			beforeHostUpdate,
			true));
	}

	[Test]
	public async Task A_locked_host_skips_with_the_reason_when_a_backup_was_wanted()
	{
		var outcome = await SkipOnALockedHost(() => Backups(true));

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Skipped, Is.True);
			Assert.That(outcome.Reason, Is.EqualTo("KeyRingLocked"));
		});
	}

	[Test]
	public async Task A_locked_host_has_nothing_to_tell_when_no_backup_was_wanted()
	{
		var outcome = await SkipOnALockedHost(() => Backups(false));

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Skipped, Is.True);
			Assert.That(outcome.Reason, Is.Null);
		});
	}

	[Test]
	public async Task Unreadable_settings_never_stop_the_update_that_may_fix_the_lock()
	{
		var outcome = await SkipOnALockedHost(() => throw new IOException("disk gone"));

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Success, Is.True);
			Assert.That(outcome.Skipped, Is.True);
			Assert.That(outcome.Reason, Is.EqualTo("KeyRingLocked"));
		});
	}
}
