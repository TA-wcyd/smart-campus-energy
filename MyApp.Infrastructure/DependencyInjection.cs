using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyApp.Core.Interfaces;
using MyApp.Infrastructure.AI;
using MyApp.Infrastructure.Configuration;
using MyApp.Infrastructure.Data;

namespace MyApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Options Configuration
        services.Configure<LlmOptions>(config.GetSection("Llm"));
        services.Configure<SupabaseOptions>(config.GetSection("Supabase"));

        var supabaseSection = config.GetSection("Supabase").Get<SupabaseOptions>() ?? new SupabaseOptions();
        var envVarName = supabaseSection.ConnectionStringEnvVar;

        var connStr = Environment.GetEnvironmentVariable(envVarName)
                      ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING")
                      ?? Environment.GetEnvironmentVariable("DATABASE_URL")
                      ?? config.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connStr) ||
            connStr.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
            connStr.StartsWith("your-", StringComparison.OrdinalIgnoreCase))
        {
            // Fallback for tests and offline development
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("GridWiseDb"));
        }
        else
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connStr));
        }

        // Data Repositories
        services.AddScoped<IEnergyRepository, EnergyRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // AI & Optimization Services
        services.AddScoped<IOptimizerService, OptimizerService>();
        services.AddScoped<IScheduleValidator, ScheduleValidator>();

        // LLM HTTP Client
        services.AddHttpClient<ILlmService, LlmService>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl) && Uri.TryCreate(opts.BaseUrl, UriKind.Absolute, out var uri))
            {
                http.BaseAddress = uri;
            }
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 20);
        });

        return services;
    }
}
