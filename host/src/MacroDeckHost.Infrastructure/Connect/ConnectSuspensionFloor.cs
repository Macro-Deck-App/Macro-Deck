using System.Globalization;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Connect;

public sealed class ConnectSuspensionFloor : IConnectSuspensionFloor
{
	private readonly IServiceScopeFactory _scopeFactory;

	public ConnectSuspensionFloor(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

	public async Task<DateTimeOffset?> Read(CancellationToken cancellationToken = default)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var value = (await preferences.GetByKey(AppPreferenceService.ConnectSuspensionRetryNotBeforeKey))?.Value;

		return DateTimeOffset.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out var parsed)
			? parsed
			: null;
	}

	public async Task Write(DateTimeOffset notBefore, CancellationToken cancellationToken = default)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();

		await preferences.SetValue(AppPreferenceService.ConnectSuspensionRetryNotBeforeKey,
			notBefore.ToString("O", CultureInfo.InvariantCulture));
	}
}
