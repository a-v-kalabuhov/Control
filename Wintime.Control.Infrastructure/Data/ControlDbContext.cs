using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using Wintime.Control.SDK;

namespace Wintime.Control.Infrastructure.Data;

public class ControlDbContext : IdentityDbContext<User>
{
    private readonly IReadOnlyList<IAppModule> _modules;

    public ControlDbContext(
        DbContextOptions<ControlDbContext> options,
        IReadOnlyList<IAppModule>? modules = null)
        : base(options)
    {
        _modules = modules ?? [];
    }

    // DbSets (Таблицы)
    public DbSet<Imm> Imms { get; set; }
    public DbSet<Mold> Molds { get; set; }
    public DbSet<Core.Entities.ShiftTask> Tasks { get; set; }
    public DbSet<Template> Templates { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<DowntimeReason> DowntimeReasons { get; set; }
    public DbSet<Telemetry> Telemetry { get; set; }
    public DbSet<ImmStatusHistory> ImmStatusHistory { get; set; }
    public DbSet<AppHeartbeat> AppHeartbeat { get; set; }
    public DbSet<Shift> Shifts { get; set; }
    public DbSet<ImmCycle> ImmCycles { get; set; }

    // Платформенные таблицы
    public DbSet<AppModuleRecord> AppModules { get; set; }
    public DbSet<SystemConfigEntry> SystemConfig { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Template>()
            .Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAt = DateTime.UtcNow;

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Конфигурация Imm
        builder.Entity<Imm>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InventoryNumber).IsUnique();
            entity.HasOne(e => e.Template).WithMany(t => t.Imms).HasForeignKey(e => e.TemplateId);
        });

        // Конфигурация Mold
        builder.Entity<Mold>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FormId).IsUnique();
        });

        // Конфигурация Task
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Imm).WithMany(i => i.Tasks).HasForeignKey(e => e.ImmId);
            entity.HasOne(e => e.Mold).WithMany(m => m.Tasks).HasForeignKey(e => e.MoldId);
            entity.HasOne(e => e.Personnel).WithMany(p => p.AssignedTasks).HasForeignKey(e => e.PersonnelId).OnDelete(DeleteBehavior.SetNull);
        });

        // Конфигурация Telemetry (Оптимизация)
        builder.Entity<Telemetry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ImmId, e.Timestamp });
            entity.HasIndex(e => e.ParameterName);
            // Для MVP пока без партиционирования, но индекс обязателен
        });

        // Конфигурация Event
        builder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Imm).WithMany(i => i.Events).HasForeignKey(e => e.ImmId);
        });
        
        // Настройка имен таблиц (опционально, чтобы были во множественном числе)
        builder.Entity<Imm>().ToTable("Imms");
        builder.Entity<Mold>().ToTable("Molds");
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>().ToTable("Tasks");
        builder.Entity<User>().ToTable("Users");

        // Конфигурация ImmCycle
        builder.Entity<ImmCycle>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ImmId, e.StartTime });
            entity.HasOne(e => e.Imm).WithMany().HasForeignKey(e => e.ImmId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Task).WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Mold).WithMany().HasForeignKey(e => e.MoldId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.StartTime).HasColumnType("timestamp with time zone");
            entity.Property(e => e.EndTime).HasColumnType("timestamp with time zone");
            entity.ToTable("ImmCycles");
        });

        // Конфигурация ImmStatusHistory
        builder.Entity<ImmStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityByDefaultColumn();
            entity.HasIndex(e => new { e.ImmId, e.ChangedAt });
            entity.HasOne(e => e.Imm)
                  .WithMany()
                  .HasForeignKey(e => e.ImmId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ChangedAt).HasColumnType("timestamp with time zone");
            entity.Property(e => e.EndedAt).HasColumnType("timestamp with time zone");
            entity.ToTable("ImmStatusHistory");
        });

        // Конфигурация AppHeartbeat (single-row sentinel, Id = 1 always)
        builder.Entity<AppHeartbeat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.LastHeartbeatAt).HasColumnType("timestamp with time zone");
            entity.ToTable("AppHeartbeat");
        });

        // Платформенные сущности
        builder.Entity<AppModuleRecord>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.ToTable("AppModules");
        });

        builder.Entity<SystemConfigEntry>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone");
            entity.ToTable("SystemConfig");
        });

        // Модульные конфигурации сущностей
        foreach (var module in _modules)
            module.ConfigureModel(builder);
    }
}