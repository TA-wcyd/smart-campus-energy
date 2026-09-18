using Microsoft.EntityFrameworkCore;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.Data;

/// <summary>
/// EF Core DbContext for the Smart-Campus GridWise application.
/// All table and column names use snake_case to match the Supabase/PostgreSQL convention.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User>          Users          => Set<User>();
    public DbSet<ChatMessage>   ChatMessages   => Set<ChatMessage>();
    public DbSet<EnergyScenario> EnergyScenarios => Set<EnergyScenario>();
    public DbSet<EnergyPlan>    EnergyPlans    => Set<EnergyPlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Users ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id)           .HasColumnName("id");
            e.Property(u => u.Email)        .HasColumnName("email")        .IsRequired().HasMaxLength(255);
            e.Property(u => u.DisplayName)  .HasColumnName("display_name") .IsRequired().HasMaxLength(255);
            e.Property(u => u.CreatedAtUtc) .HasColumnName("created_at_utc");
            e.HasIndex(u => u.Email).IsUnique();
        });

        // ── ChatMessages ───────────────────────────────────────────────────────
        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.ToTable("chat_messages");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id)          .HasColumnName("id");
            e.Property(m => m.UserId)      .HasColumnName("user_id");
            e.Property(m => m.Role)        .HasColumnName("role")         .IsRequired().HasMaxLength(50);
            e.Property(m => m.Content)     .HasColumnName("content")      .IsRequired();
            e.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc");

            // FK → users
            e.HasOne<User>()
             .WithMany()
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── EnergyScenarios ───────────────────────────────────────────────────
        modelBuilder.Entity<EnergyScenario>(e =>
        {
            e.ToTable("energy_scenarios");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id)                 .HasColumnName("id");
            e.Property(s => s.ScenarioId)         .HasColumnName("scenario_id")          .IsRequired().HasMaxLength(100);
            e.Property(s => s.OperatorNotesJson)  .HasColumnName("operator_notes_json")  .IsRequired();
            e.Property(s => s.HoursJson)          .HasColumnName("hours_json")           .IsRequired();
            e.Property(s => s.BatteryJson)        .HasColumnName("battery_json")         .IsRequired();
            e.Property(s => s.CreatedAtUtc)       .HasColumnName("created_at_utc");
            e.HasIndex(s => s.ScenarioId).IsUnique();
        });

        // ── EnergyPlans ───────────────────────────────────────────────────────
        modelBuilder.Entity<EnergyPlan>(e =>
        {
            e.ToTable("energy_plans");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id)                          .HasColumnName("id");
            e.Property(p => p.ScenarioId)                  .HasColumnName("scenario_id");
            e.Property(p => p.DirectiveInterpretationJson) .HasColumnName("directive_interpretation_json").IsRequired();
            e.Property(p => p.HourlyPlanJson)              .HasColumnName("hourly_plan_json")             .IsRequired();
            e.Property(p => p.TotalGridKwh)                .HasColumnName("total_grid_kwh");
            e.Property(p => p.TotalCostBdt)                .HasColumnName("total_cost_bdt");
            e.Property(p => p.PeakGridKwh)                 .HasColumnName("peak_grid_kwh");
            e.Property(p => p.PlanSummary)                 .HasColumnName("plan_summary")                .IsRequired();
            e.Property(p => p.CreatedAtUtc)                .HasColumnName("created_at_utc");

            // FK → energy_scenarios
            e.HasOne<EnergyScenario>()
             .WithMany()
             .HasForeignKey(p => p.ScenarioId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
