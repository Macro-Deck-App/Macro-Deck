using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Files;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class UserVariableWriterFileTests
{
	private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "macro-deck-writer", "value.txt");

	private FakeVariableFileSystem _files = null!;
	private FileVariableSynchronizer _synchronizer = null!;
	private VariableService _service = null!;
	private ServiceProvider _provider = null!;
	private UserVariableWriter _writer = null!;

	[SetUp]
	public void SetUp()
	{
		var registry = new VariableRegistry();
		var mediator = new RecordingMediator();
		_files = new FakeVariableFileSystem();
		_synchronizer = new FileVariableSynchronizer(registry, mediator, _files, Logger.None, TimeSpan.Zero, TimeSpan.Zero);
		_service = TestVariableServices.Create(registry, new NullStore(), mediator, files: _synchronizer);
		_provider = new ServiceCollection()
			.AddScoped<IVariableService>(_ => _service)
			.AddSingleton(TestLocalization.Resolver)
			.AddSingleton(TestLocalization.Preferences)
			.BuildServiceProvider();
		_writer = new UserVariableWriter(_provider.GetRequiredService<IServiceScopeFactory>());
	}

	[TearDown]
	public void TearDown()
	{
		_writer.Dispose();
		_provider.Dispose();
		_synchronizer.Dispose();
	}

	[Test]
	public async Task Set_on_a_normal_user_variable_is_still_applied()
	{
		await _service.CreateUserVariable("count", VariableScope.Global, null, DomainVariableType.Numeric, 1, null);

		var result = await _writer.ApplyAsync("count", null, UserVariableOperation.Set, "2");

		Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
	}

	[Test]
	public async Task Set_on_a_read_only_file_variable_is_not_editable_with_a_readable_message()
	{
		_files.SetFile(FilePath, "1", notify: false);
		await CreateFileVariable(allowWriteBack: false);

		var result = await _writer.ApplyAsync("level", null, UserVariableOperation.Set, "2");
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(async () =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.NotEditable));
			Assert.That(result.Message,
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Variables.Errors.VariableReadOnly())));
			Assert.That((await _service.Resolve("level", VariableScope.Global, null))!.Value, Is.EqualTo("1"));
			Assert.That(_files.Writes, Is.Empty);
		});
	}

	[Test]
	public async Task Add_on_a_write_back_file_variable_is_applied_and_written_to_the_file()
	{
		_files.SetFile(FilePath, "1", notify: false);
		await CreateFileVariable(allowWriteBack: true);

		var result = await _writer.ApplyAsync("level", null, UserVariableOperation.Add, "2");
		await _synchronizer.WhenIdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(_files.Content(FilePath), Is.EqualTo("3"));
		});
	}

	private async Task CreateFileVariable(bool allowWriteBack)
		=> await _service.CreateUserVariable("level",
			VariableScope.Global,
			null,
			DomainVariableType.Numeric,
			null,
			null,
			new VariableFileSource(FilePath, allowWriteBack));

	private sealed class NullStore : MacroDeckHost.Application.Persistence.IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
