using Microsoft.EntityFrameworkCore;
using MyApp.Infrastructure.Data;
using Npgsql;
using Xunit;

namespace MyApp.Tests;

public class DatabaseConnectionTests
{
    private const string ConnectionString = "Host=aws-0-ap-northeast-2.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.tqykimpjviquhajxarpn;Password=SmartCampus123!;SSL Mode=Require;Trust Server Certificate=true;";

    [Fact]
    public async Task CanConnectToSupabaseSessionPooler()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT version();", conn);
        var version = await cmd.ExecuteScalarAsync();

        Assert.NotNull(version);
        Assert.Contains("PostgreSQL", version.ToString());
    }

    [Fact]
    public async Task CanConnectThroughDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var dbContext = new AppDbContext(options);
        var canConnect = await dbContext.Database.CanConnectAsync();

        Assert.True(canConnect, "DbContext failed to connect to Supabase PostgreSQL.");
    }
}
