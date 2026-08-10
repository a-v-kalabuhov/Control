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
    public DbSet<Core.Entities.ShiftTask> ShiftTasks { get; set; }
    public DbSet<Template> Templates { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<DowntimeReason> DowntimeReasons { get; set; }
    public DbSet<Telemetry> Telemetry { get; set; }
    public DbSet<ImmStatusHistory> ImmStatusHistory { get; set; }
    public DbSet<AppHeartbeat> AppHeartbeat { get; set; }
    public DbSet<Shift> Shifts { get; set; }
    public DbSet<ImmCycle> ImmCycles { get; set; }
    public DbSet<ProductType> ProductTypes { get; set; }
    public DbSet<UnplannedRun> UnplannedRuns { get; set; }
    public DbSet<Order> Orders { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Template>()
            .Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAt = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ShiftTask>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified))
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
            entity.HasOne(e => e.ProductType)
                  .WithMany(pt => pt.Molds)
                  .HasForeignKey(e => e.ProductTypeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // Конфигурация ProductType (PZP-09)
        builder.Entity<ProductType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Article).IsUnique();
        });
        builder.Entity<ProductType>().ToTable("ProductTypes");

        // Конфигурация Task
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Imm).WithMany(i => i.ShiftTasks).HasForeignKey(e => e.ImmId);
            entity.HasOne(e => e.Mold).WithMany(m => m.ShiftTasks).HasForeignKey(e => e.MoldId);
            entity.HasOne(e => e.Personnel).WithMany(p => p.AssignedTasks).HasForeignKey(e => e.PersonnelId).OnDelete(DeleteBehavior.SetNull);
        });

        // Конфигурация Telemetry (Оптимизация)
        builder.Entity<Telemetry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ImmId, e.Timestamp });
            entity.HasIndex(e => e.ParameterName);
            // BL-23: покрывающий индекс под «окно + выбранные сигналы одного ТПА».
            entity.HasIndex(e => new { e.ImmId, e.ParameterName, e.Timestamp })
                  .HasDatabaseName("IX_Telemetry_Imm_Param_Time");
        });

        // Конфигурация Event
        builder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Imm).WithMany(i => i.Events).HasForeignKey(e => e.ImmId);
            entity.HasOne(e => e.Task).WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.SetNull);
        });
        
        // Настройка имен таблиц (опционально, чтобы были во множественном числе)
        builder.Entity<Imm>().ToTable("Imms");
        builder.Entity<Mold>().ToTable("Molds");
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>().ToTable("ShiftTasks");
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
            entity.Property(e => e.InjectionStartTime).HasColumnType("timestamp with time zone");
            entity.ToTable("ImmCycles");
        });

        // Идентичность цикла (Number, StartTime) — опора для ЗА-проверки перед открытием
        // строки и защита от дублей на уровне БД (контракт v2, счётчик коннектора легитимно
        // обнуляется между сериями выпуска без разрыва связи, поэтому один Number не уникален).
        builder.Entity<ImmCycle>()
            .HasIndex(e => new { e.ImmId, e.CycleNumber, e.StartTime })
            .IsUnique()
            .HasFilter("\"CycleNumber\" IS NOT NULL")
            .HasDatabaseName("IX_ImmCycles_Imm_CycleNumber_StartTime");

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

        // Конфигурация UnplannedRun (PZP-04)
        builder.Entity<UnplannedRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Imm).WithMany().HasForeignKey(e => e.ImmId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AssignedTask).WithMany().HasForeignKey(e => e.AssignedTaskId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.StartTime).HasColumnType("timestamp with time zone");
            entity.Property(e => e.ClosedAt).HasColumnType("timestamp with time zone");
            entity.Property(e => e.AssignedAt).HasColumnType("timestamp with time zone");
            // Partial-индекс: поиск открытого эпизода ТПА (крошечный, только открытые)
            entity.HasIndex(e => e.ImmId).HasFilter("\"ClosedAt\" IS NULL").HasDatabaseName("IX_UnplannedRuns_Imm_Open");
            entity.ToTable("UnplannedRuns");
        });

        // Partial-индекс на ImmCycles под агрегат деривации и счётчик дашборда
        builder.Entity<ImmCycle>()
            .HasIndex(e => new { e.ImmId, e.EndTime })
            .HasFilter("\"TaskId\" IS NULL")
            .HasDatabaseName("IX_ImmCycles_Imm_Orphan");

        // Конфигурация Order (PZP-05, ADR-0009)
        builder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.ProductType)
                  .WithMany()
                  .HasForeignKey(e => e.ProductTypeId)
                  .OnDelete(DeleteBehavior.Restrict);   // ProductType физически не удаляют (IsActive)
            entity.Property(e => e.OrderDate).HasColumnType("timestamp with time zone");
            entity.Property(e => e.DueDate).HasColumnType("timestamp with time zone");
            entity.HasIndex(e => e.Number);              // Number НЕ уникален (решение п.2) — обычный индекс
            entity.ToTable("Orders");
        });

        // Связь ShiftTask → Order (1:N, OrderId nullable)
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>()
            .HasOne(t => t.Order)
            .WithMany(o => o.Tasks)
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>()
            .HasIndex(t => t.OrderId);                   // под Σ-агрегацию прогресса
    }
}