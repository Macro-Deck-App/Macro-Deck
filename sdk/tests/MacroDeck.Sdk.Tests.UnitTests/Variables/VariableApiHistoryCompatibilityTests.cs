using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Sdk.Tests.UnitTests.Variables;

[TestFixture]
public class VariableApiHistoryCompatibilityTests
{
	[Test]
	public void History_on_an_api_written_before_it_is_reported_as_unsupported()
	{
		IVariableApi api = new VariableApiWrittenBeforeHistory();

		var exception = Assert.ThrowsAsync<VariableHistoryException>(() => api.OpenHistoryAsync("viewer_count", 40));

		Assert.That(exception!.ErrorCode, Is.EqualTo(VariableHistoryErrorCode.Unsupported));
	}

	[Test]
	public async Task Widget_data_on_an_api_written_before_it_is_not_known()
	{
		IWidgetApi api = new WidgetApiWrittenBeforeData();

		Assert.That(await api.GetWidgetDataAsync(Guid.NewGuid().ToString()), Is.Null);
	}

	private sealed class VariableApiWrittenBeforeHistory : IVariableApi
	{
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
	}

	private sealed class WidgetApiWrittenBeforeData : IWidgetApi
	{
		public IReadOnlyList<WidgetTargetInfo> GetWidgets()
		{
			return [];
		}

		public bool Exists(string widgetId)
		{
			return false;
		}

		public Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}
	}
}
