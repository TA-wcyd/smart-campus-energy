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
        // Database connection string from .env or config
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                               ?? config.GetConnectionString("DefaultConnection")
                               ?? config["Supabase:ConnectionStringEnvVar"];

        if (!string.IsNullOrWhiteSpace(connectionString) && !connectionString.StartsWith("YOUR_"))
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
