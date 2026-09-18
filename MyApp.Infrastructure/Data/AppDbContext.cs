using Microsoft.EntityFrameworkCore;
using MyApp.Core.Models;

namespace MyApp.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<EnergyScenario> EnergyScenarios => Set<EnergyScenario>();
    public DbSet<EnergyPlan> EnergyPlans => Set<EnergyPlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).HasColumnName("id");
            entity.Property(u => u.Email).HasColumnName("email").IsRequired().HasMaxLength(255);
            entity.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(255);
            entity.Property(u => u.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasIndex(u => u.Email).IsUnique();
        });

        // ChatMessage
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("chat_messages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).HasColumnName("id");
            entity.Property(m => m.UserId).HasColumnName("user_id");
            entity.Property(m => m.Role).HasColumnName("role").IsRequired().HasMaxLength(50);
            entity.Property(m => m.Content).HasColumnName("content").IsRequired();
            entity.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EnergyScenario
        modelBuilder.Entity<EnergyScenario>(entity =>
        {
            entity.ToTable("energy_scenarios");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ScenarioId).HasColumnName("scenario_id").IsRequired().HasMaxLength(100);
            entity.Property(e => e.OperatorNotesJson).HasColumnName("operator_notes_json");
            entity.Property(e => e.HoursJson).HasColumnName("hours_json");
            entity.Property(e => e.BatteryJson).HasColumnName("battery_json");
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasIndex(e => e.ScenarioId).IsUnique();
        });

        // EnergyPlan
        modelBuilder.Entity<EnergyPlan>(entity =>
        {
            entity.ToTable("energy_plans");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.ScenarioId).HasColumnName("scenario_id");
            entity.Property(p => p.DirectiveInterpretationJson).HasColumnName("directive_interpretation_json");
            entity.Property(p => p.HourlyPlanJson).HasColumnName("hourly_plan_json");
            entity.Property(p => p.TotalGridKwh).HasColumnName("total_grid_kwh");
            entity.Property(p => p.TotalCostBdt).HasColumnName("total_cost_bdt");
            entity.Property(p => p.PeakGridKwh).HasColumnName("peak_grid_kwh");
            entity.Property(p => p.PlanSummary).HasColumnName("plan_summary");
            entity.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc");

            entity.HasOne<EnergyScenario>()
                .WithMany()
                .HasForeignKey(p => p.ScenarioId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
