using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using MyApp.Api.Middleware;
using MyApp.Infrastructure;
using MyApp.Infrastructure.Data;

// Load .env variables
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Dynamic PORT handling for cloud hosting environments (e.g., Render)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add Controllers with snake_case JSON serialization
builder.Services.AddControllers()
    .AddApplicationPart(typeof(MyApp.Api.Controllers.HealthController).Assembly)
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// API Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Infrastructure Layer (Database, LLM, Optimizer, Validator, Repositories)
builder.Services.AddInfrastructure(builder.Configuration);

// UI Pages
builder.Services.AddRazorPages()
    .AddApplicationPart(typeof(MyApp.Api.Controllers.HealthController).Assembly);

// CORS for local development
builder.Services.AddCors(options =>
{
    options.AddPolicy("UiPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Global Exception Handler
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Ensure Database schema is created
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Could not ensure database created automatically.");
    }
}

// Configure Middleware Pipeline
app.UseExceptionHandler();

if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseRouting();
app.UseCors("UiPolicy");

app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

// Fallback health check
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Sample Scenario Endpoint
app.MapGet("/samples/scenario", () =>
    Results.File(
        Path.Combine(app.Environment.ContentRootPath, "wwwroot", "samples", "sample-scenario.json"),
        "application/json"));

app.Run();

public partial class Program { }
