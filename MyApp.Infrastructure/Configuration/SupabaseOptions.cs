namespace MyApp.Infrastructure.Configuration;

public sealed class SupabaseOptions
{
    public string ConnectionStringEnvVar { get; set; } = "SUPABASE_CONNECTION_STRING";
    public bool EnableSensitiveDataLogging { get; set; } = false;
}
