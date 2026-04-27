using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Infrastructure.Data;

public class ControlDbContext : IdentityDbContext<User>
{
    public ControlDbContext(DbContextOptions<ControlDbContext> options)
        : base(options)
    {
    }

    // DbSets (Таблицы)
    public DbSet<Imm> Imms { get; set; }
    public DbSet<Mold> Molds { get; set; }
    public DbSet<Core.Entities.Task> Tasks { get; set; }
    public DbSet<Template> Templates { get; set; }
    public DbSet<MoldUsage> MoldUsages { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<DowntimeReason> DowntimeReasons { get; set; }
    public DbSet<Telemetry> Telemetry { get; set; }

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
        builder.Entity<Wintime.Control.Core.Entities.Task>(entity =>
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
        builder.Entity<Wintime.Control.Core.Entities.Task>().ToTable("Tasks");
        builder.Entity<User>().ToTable("Users");
    }
}