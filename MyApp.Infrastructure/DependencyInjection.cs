using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyApp.Core.Interfaces;
using MyApp.Infrastructure.AI;
using MyApp.Infrastructure.Configuration;
using MyApp.Infrastructure.Data;

namespace MyApp.Infrastructure;

/// <summary>
/// Extension method to register all Infrastructure services.
/// This is the ONLY place that wires LLM, Optimizer, Repositories.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ── Options ───────────────────────────────────────────────────────────
        services.Configure<LlmOptions>(config.GetSection("Llm"));
        services.Configure<SupabaseOptions>(config.GetSection("Supabase"));

        // ── Database ──────────────────────────────────────────────────────────
        var supabaseOpts = config.GetSection("Supabase").Get<SupabaseOptions>()
                           ?? new SupabaseOptions();

        // Support DATABASE_URL as the primary alias used in .env
        var connStr = Environment.GetEnvironmentVariable("DATABASE_URL")
                      ?? Environment.GetEnvironmentVariable(supabaseOpts.ConnectionStringEnvVar);

        if (!string.IsNullOrWhiteSpace(connStr))
        {
            services.AddDbContext<AppDbContext>(opt =>
            {
                opt.UseNpgsql(connStr);
                if (supabaseOpts.EnableSensitiveDataLogging)
                    opt.EnableSensitiveDataLogging();
            });
        }
        else
        {
            // Fallback to InMemory if no connection string is set (dev/test)
            services.AddDbContext<AppDbContext>(opt =>
                opt.UseInMemoryDatabase("GridWiseDb"));
        }

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<IEnergyRepository, EnergyRepository>();
        services.AddScoped<IUserRepository,   UserRepository>();

        // ── Optimizer & Validator ─────────────────────────────────────────────
        services.AddScoped<IOptimizerService,  OptimizerService>();
        services.AddScoped<IScheduleValidator, ScheduleValidator>();

        // ── LLM Service ───────────────────────────────────────────────────────
        // LlmService uses IConfiguration + ILogger (no HttpClient yet).
        // When Member 2 upgrades to real HTTP calls, swap to AddHttpClient here.
        services.AddScoped<ILlmService, LlmService>();

        return services;
    }
}
