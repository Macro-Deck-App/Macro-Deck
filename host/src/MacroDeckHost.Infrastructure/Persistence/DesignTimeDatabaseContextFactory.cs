using Microsoft.EntityFrameworkCore.Design;
using Serilog.Core;

namespace MacroDeckHost.Infrastructure.Persistence;

public class DesignTimeDatabaseContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
	public DatabaseContext CreateDbContext(string[] args)
	{
		return new DatabaseContext(Logger.None, Path.Combine(Path.GetTempPath(), "macrodeck-design-time.db"));
	}
}
