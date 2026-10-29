using MacroDeckHost.Application.Paths;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Persistence.EntityConfigurations;
using MacroDeckHost.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Persistence;

public class DatabaseContext : DbContext
{
	private static readonly ILoggerFactory _loggerFactory = new LoggerFactory().AddSerilog();
	private static readonly SaveChangesSetTimestampsInterceptor _timestampsInterceptor = new();
	private static readonly string _migrationsAssembly = typeof(DatabaseContext).Assembly.GetName().Name!;
	private static int _pathLogged;

	private readonly string? _databasePath;
	private readonly ILogger _logger;
	private readonly IMacroDeckPaths? _paths;

	public DatabaseContext(ILogger logger, IMacroDeckPaths paths)
	{
		_logger = logger;
		_paths = paths;
	}

	internal DatabaseContext(ILogger logger, string databasePath)
	{
		_logger = logger;
		_databasePath = databasePath;
	}

	public DbSet<SecretEntity> Secrets => Set<SecretEntity>();

	public DbSet<IntegrationConfigEntryEntity> IntegrationConfigEntries => Set<IntegrationConfigEntryEntity>();

	public DbSet<AppPreferenceEntity> AppPreferences => Set<AppPreferenceEntity>();

	public DbSet<UserEntity> Users => Set<UserEntity>();

	public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();

	public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();

	public DbSet<PluginAccessTokenEntity> PluginAccessTokens => Set<PluginAccessTokenEntity>();

	public DbSet<McpAccessTokenEntity> McpAccessTokens => Set<McpAccessTokenEntity>();

	public DbSet<PluginRegistrationEntity> PluginRegistrations => Set<PluginRegistrationEntity>();

	public DbSet<PluginTrustRecordEntity> PluginTrustRecords => Set<PluginTrustRecordEntity>();

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		var databasePath = _databasePath ?? _paths!.DatabasePath;

		if (_databasePath is null && Interlocked.Exchange(ref _pathLogged, 1) == 0)
		{
			_logger.Information("Database path: {DbPath}", databasePath);
		}

		// Unpooled until Microsoft.Data.Sqlite ships dotnet/efcore#39012: its pool can hand out a connection
		// whose sqlite3 handle was already disposed (dotnet/efcore#39008).
		var connectionString =
			new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
		optionsBuilder.UseSqlite(connectionString, b => b.MigrationsAssembly(_migrationsAssembly))
			.UseLoggerFactory(_loggerFactory)
			.AddInterceptors(_timestampsInterceptor);
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(BaseEntityConfig<>).Assembly);
	}
}
