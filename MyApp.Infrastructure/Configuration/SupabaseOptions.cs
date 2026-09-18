namespace MyApp.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the Supabase / PostgreSQL connection.
/// Bind from appsettings.json section "Supabase".
/// </summary>
public sealed class SupabaseOptions
{
    /// <summary>
    /// Name of the environment variable that holds the Npgsql connection string.
    /// Default: SUPABASE_CONNECTION_STRING (set DATABASE_URL as an alias in .env).
    /// </summary>
    public string ConnectionStringEnvVar { get; set; } = "SUPABASE_CONNECTION_STRING";

    /// <summary>
    /// When true, EF Core will log parameter values — never enable in production.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; } = false;
}
