using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.Tests.UnitTests.Variables;

[TestFixture]
public class VariableApiDefinitionRefreshAndBindingCompatibilityTests
{
	[Test]
	public async Task A_definition_refresh_on_an_api_written_before_it_reads_every_variable()
	{
		var api = new ApiWrittenBeforeDefinitionRefresh();

		await ((IVariableApi)api).RequestDefinitionRefreshAsync(["volume-percent"]);

		Assert.That(api.RefreshRequests, Is.EqualTo(1));
	}

	[Test]
	public async Task A_definition_refresh_naming_nothing_asks_for_nothing()
	{
		var api = new ApiWrittenBeforeDefinitionRefresh();

		await ((IVariableApi)api).RequestDefinitionRefreshAsync([]);

		Assert.That(api.RefreshRequests, Is.Zero);
	}

	[Test]
	public void Binding_on_an_api_written_before_it_reports_it_as_not_supported()
	{
		IVariableApi api = new ApiWrittenBeforeDefinitionRefresh();

		Assert.ThrowsAsync<NotSupportedException>(() =>
			api.BindAsync([new VariableBindingRequest("entity/light.kitchen/state", "kitchen", VariableType.Text)]));
	}

	private sealed class ApiWrittenBeforeDefinitionRefresh : IVariableApi
	{
		public int RefreshRequests { get; private set; }

		public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
		{
			throw new NotSupportedException();
		}

		public Task<VariableHandle?> GetByNameAsync(string name)
		{
			throw new NotSupportedException();
		}

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
		{
			throw new NotSupportedException();
		}

		public Task SetValueAsync(Guid variableId, object? value)
		{
			throw new NotSupportedException();
		}

		public Task DeleteAsync(Guid variableId)
		{
			throw new NotSupportedException();
		}

		public Task RequestRefreshAsync(CancellationToken cancellationToken = default)
		{
			RefreshRequests++;
			return Task.CompletedTask;
		}
	}
}
