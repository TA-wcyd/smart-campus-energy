using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Core.Interfaces;
using MyApp.Core.Models;
using MyApp.Infrastructure.AI;
using MyApp.Infrastructure.Data;

namespace MyApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Database connection string from environment variables or configuration
        var envVarName = config["Supabase:ConnectionStringEnvVar"] ?? "SUPABASE_CONNECTION_STRING";
        var connectionString = Environment.GetEnvironmentVariable(envVarName)
                               ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING")
                               ?? Environment.GetEnvironmentVariable("DATABASE_URL")
                               ?? config.GetConnectionString("DefaultConnection")
                               ?? config["Supabase:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(connectionString) &&
            !connectionString.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("your-", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString));
        }
        else
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("GridWiseDb"));
        }

        // Repositories & Services
        services.AddScoped<ILlmService, LlmService>();
        services.AddScoped<IOptimizerService, OptimizerService>();
        services.AddScoped<IScheduleValidator, ScheduleValidator>();
        services.AddScoped<IEnergyRepository, EnergyRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}
