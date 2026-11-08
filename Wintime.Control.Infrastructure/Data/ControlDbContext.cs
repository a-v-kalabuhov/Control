using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

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
    public DbSet<Product> Products { get; set; }
    public DbSet<UnplannedRun> UnplannedRuns { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<PlanningDefaults> PlanningDefaults { get; set; }
    public DbSet<Material> Materials { get; set; }
    public DbSet<ProductRecipeIngredient> ProductRecipeIngredients { get; set; }
    public DbSet<ShiftTaskRecipeIngredient> ShiftTaskRecipeIngredients { get; set; }
    public DbSet<ShiftPlanDay> ShiftPlanDays { get; set; }
    public DbSet<ShiftPlanItem> ShiftPlanItems { get; set; }
    public DbSet<ShiftImmOperatorAssignment> ShiftImmOperatorAssignments { get; set; }

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

            // Совместимые ТПА (PZP-16): many-to-many без класса связи, навигация только у Mold.
            entity.HasMany(e => e.CompatibleImms)
                  .WithMany()
                  .UsingEntity<Dictionary<string, object>>(
                      "MoldCompatibleImms",
                      r => r.HasOne<Imm>().WithMany().HasForeignKey("ImmId").OnDelete(DeleteBehavior.Restrict),
                      l => l.HasOne<Mold>().WithMany().HasForeignKey("MoldId").OnDelete(DeleteBehavior.Cascade),
                      j =>
                      {
                          j.HasKey("MoldId", "ImmId");
                          j.HasIndex("ImmId");
                      });
        });

        // Конфигурация ProductType (PZP-09)
        builder.Entity<ProductType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Article).IsUnique();
        });
        builder.Entity<ProductType>().ToTable("ProductTypes");

        // Конфигурация Product (PZP-19, ADR-0020): изделие внутри типа; FullArticle уникален в БД.
        builder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.ProductType)
                  .WithMany(pt => pt.Products)
                  .HasForeignKey(e => e.ProductTypeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.FullArticle).IsUnique();
            entity.ToTable("Products");
        });

        // Конфигурация Material (PZP-22): уникальность внутри типа.
        // Регистронезависимый индекс (Type, lower(coalesce(Manufacturer,'')), lower(Name)) — на выражении,
        // EF его не моделирует, создаётся сырым SQL в миграции AddMaterials.
        builder.Entity<Material>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Article).HasMaxLength(MaterialRules.ArticleMaxLength).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(MaterialRules.NameMaxLength).IsRequired();
            entity.Property(e => e.Manufacturer).HasMaxLength(MaterialRules.ManufacturerMaxLength);
            entity.Property(e => e.ColorName).HasMaxLength(MaterialRules.ColorNameMaxLength);
            entity.Property(e => e.ColorCode).HasMaxLength(MaterialRules.ColorCodeMaxLength);
            entity.Property(e => e.Comment).HasMaxLength(MaterialRules.CommentMaxLength);
            entity.HasIndex(e => new { e.Type, e.Article }).IsUnique();
            entity.HasIndex(e => new { e.Type, e.IsActive });
            entity.ToTable("Materials");
        });

        // BL-36: рецепт изделия — строки-проценты. Cascade нужен для физического удаления
        // изделия без ссылок (исключение мягкого удаления); материал из рецепта не удаляют.
        builder.Entity<ProductRecipeIngredient>(entity =>
        {
            entity.HasKey(e => e.Id);
            // BaseEntity присваивает Guid в конструкторе, поэтому без ValueGeneratedNever EF
            // считает строку, добавленную в коллекцию загруженного изделия, «изменённой» (UPDATE).
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasOne(e => e.Product).WithMany(p => p.RecipeIngredients)
                  .HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Material).WithMany()
                  .HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Percent).HasColumnType("numeric(5,2)");
            entity.HasIndex(e => new { e.ProductId, e.MaterialId }).IsUnique();
            entity.ToTable("ProductRecipeIngredients");
        });

        // BL-36: копия рецепта в задании. OriginalMaterialId — материал изделия на момент
        // копирования (аналитика, паспорт партии BL-27); Restrict на оба материала.
        builder.Entity<ShiftTaskRecipeIngredient>(entity =>
        {
            entity.HasKey(e => e.Id);
            // См. комментарий выше: тот же Guid-в-конструкторе, тот же риск UPDATE вместо INSERT.
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasOne(e => e.ShiftTask).WithMany(t => t.RecipeIngredients)
                  .HasForeignKey(e => e.ShiftTaskId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Material).WithMany()
                  .HasForeignKey(e => e.MaterialId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OriginalMaterial).WithMany()
                  .HasForeignKey(e => e.OriginalMaterialId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Percent).HasColumnType("numeric(5,2)");
            entity.HasIndex(e => new { e.ShiftTaskId, e.MaterialId }).IsUnique();
            entity.ToTable("ShiftTaskRecipeIngredients");
        });

        // Конфигурация Task
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Imm).WithMany(i => i.ShiftTasks).HasForeignKey(e => e.ImmId);
            entity.HasOne(e => e.Mold).WithMany(m => m.ShiftTasks).HasForeignKey(e => e.MoldId);
            entity.HasOne(e => e.Personnel).WithMany(p => p.AssignedTasks).HasForeignKey(e => e.PersonnelId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
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
        builder.Entity<Wintime.Control.Core.Entities.ShiftTask>().ToTable("ShiftTasks", t =>
        {
            // PZP-14: пределы норматива и буфера (дублируют PlanningNormLimits на уровне БД)
            t.HasCheckConstraint("CK_ShiftTasks_PlannedSetupMinutes", "\"PlannedSetupMinutes\" BETWEEN 0 AND 1440");
            t.HasCheckConstraint("CK_ShiftTasks_ReleaseBufferFixedMinutes", "\"ReleaseBufferFixedMinutes\" BETWEEN 0 AND 1440");
            t.HasCheckConstraint("CK_ShiftTasks_ReleaseBufferPercent", "\"ReleaseBufferPercent\" BETWEEN 0 AND 100");
        });
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

        // Умолчания планирования (PZP-14, ADR-0015): одна строка, Id = 1, создаётся миграцией
        builder.Entity<PlanningDefaults>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UpdatedById).OnDelete(DeleteBehavior.SetNull);
            entity.ToTable("PlanningDefaults", t =>
            {
                t.HasCheckConstraint("CK_PlanningDefaults_Singleton", "\"Id\" = 1");
                t.HasCheckConstraint("CK_PlanningDefaults_SetupMinutes", "\"SetupMinutes\" BETWEEN 0 AND 1440");
                t.HasCheckConstraint("CK_PlanningDefaults_ReleaseBufferFixedMinutes", "\"ReleaseBufferFixedMinutes\" BETWEEN 0 AND 1440");
                t.HasCheckConstraint("CK_PlanningDefaults_ReleaseBufferPercent", "\"ReleaseBufferPercent\" BETWEEN 0 AND 100");
                t.HasCheckConstraint("CK_PlanningDefaults_ConflictThresholdMinutes", "\"ConflictThresholdMinutes\" BETWEEN 0 AND 1440");
            });
            entity.HasData(new PlanningDefaults { Id = 1, ConflictThresholdMinutes = Core.Entities.PlanningDefaults.DefaultConflictThresholdMinutes });
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
            entity.HasOne(e => e.Product)
                  .WithMany()
                  .HasForeignKey(e => e.ProductId)
                  .OnDelete(DeleteBehavior.Restrict);   // использованное изделие физически не удаляют
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

        // План смен и интервалы назначения литейщиков (PZP-32).
        builder.Entity<ShiftPlanDay>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.ShiftDate).IsUnique();
            entity.ToTable("ShiftPlanDays");
        });

        builder.Entity<ShiftPlanItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.StartAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(e => e.EndAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(e => e.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.HasOne(e => e.Day).WithMany(d => d.Items)
                  .HasForeignKey(e => e.ShiftPlanDayId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ShiftLead).WithMany()
                  .HasForeignKey(e => e.ShiftLeadId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.ShiftPlanDayId, e.Position }).IsUnique();
            entity.ToTable("ShiftPlanItems", t =>
            {
                t.HasCheckConstraint("CK_ShiftPlanItems_Position", "\"Position\" >= 1");
                t.HasCheckConstraint("CK_ShiftPlanItems_StartAtUtc_EndAtUtc", "\"StartAtUtc\" < \"EndAtUtc\"");
            });
        });

        builder.Entity<ShiftImmOperatorAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.AssignedAt).HasColumnType("timestamp with time zone");
            entity.Property(e => e.UnassignedAt).HasColumnType("timestamp with time zone");
            entity.HasOne(e => e.ShiftPlanItem).WithMany(i => i.OperatorAssignments)
                  .HasForeignKey(e => e.ShiftPlanItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Imm).WithMany()
                  .HasForeignKey(e => e.ImmId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Operator).WithMany()
                  .HasForeignKey(e => e.OperatorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.ShiftPlanItemId, e.ImmId, e.Slot })
                  .IsUnique().HasFilter("\"UnassignedAt\" IS NULL")
                  .HasDatabaseName("IX_ShiftImmOperatorAssignments_ActiveSlot");
            entity.HasIndex(e => new { e.ShiftPlanItemId, e.ImmId, e.OperatorId })
                  .IsUnique().HasFilter("\"UnassignedAt\" IS NULL")
                  .HasDatabaseName("IX_ShiftImmOperatorAssignments_ActiveOperator");
            entity.ToTable("ShiftImmOperatorAssignments", t =>
            {
                t.HasCheckConstraint("CK_ShiftImmOperatorAssignments_Slot", "\"Slot\" IN (1, 2)");
                t.HasCheckConstraint("CK_ShiftImmOperatorAssignments_UnassignedAt",
                    "\"UnassignedAt\" IS NULL OR \"UnassignedAt\" >= \"AssignedAt\"");
            });
        });
    }
}