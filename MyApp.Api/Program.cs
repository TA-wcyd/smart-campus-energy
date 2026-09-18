using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using MyApp.Core.Interfaces;
using MyApp.Infrastructure.AI;
using MyApp.Infrastructure.Data;

// Load environment variables from .env if present
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Resolve Database connection string (.env -> appsettings.json -> in-memory fallback)
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseInMemoryDatabase("MyAppDb"));
}

// Register AI Service
builder.Services.AddScoped<ILlmService, LlmService>();

// Configure OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// Automatically ensure schema exists in Supabase
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        try
        {
            dbContext.Database.EnsureCreated();
            Console.WriteLine("✅ Database connected and verified successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Database connection warning: {ex.Message}");
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
