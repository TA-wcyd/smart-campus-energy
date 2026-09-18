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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<LlmOptions>(config.GetSection("Llm"));
        services.Configure<SupabaseOptions>(config.GetSection("Supabase"));

        var supabaseOpts = config.GetSection("Supabase").Get<SupabaseOptions>()
                           ?? new SupabaseOptions();

        var envVarName = supabaseOpts.ConnectionStringEnvVar;
        var connStr = Environment.GetEnvironmentVariable(envVarName)
                      ?? Environment.GetEnvironmentVariable("DATABASE_URL")
                      ?? throw new InvalidOperationException($"Missing required database connection string environment variable '{envVarName}'.");

        services.AddDbContext<AppDbContext>(opt =>
        {
            opt.UseNpgsql(connStr);
            if (supabaseOpts.EnableSensitiveDataLogging)
            {
                opt.EnableSensitiveDataLogging();
            }
        });

        services.AddScoped<IEnergyRepository, EnergyRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOptimizerService, OptimizerService>();
        services.AddScoped<IScheduleValidator, ScheduleValidator>();

        services.AddHttpClient<ILlmService, LlmService>((sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            http.BaseAddress = new Uri(opts.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });

        return services;
    }
}
