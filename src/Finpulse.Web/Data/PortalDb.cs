using Finpulse.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Web.Data;

public sealed class PortalDb(DbContextOptions<PortalDb> options) : DbContext(options)
{
    public DbSet<Student> Users => Set<Student>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<SpendingPlan> Budgets => Set<SpendingPlan>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        var amountLimit = Database.IsSqlite() ? "100000000000" : "1000000000";
        m.Entity<Student>().ToTable("USUARIOS", t => t.HasCheckConstraint("CK_USER_WARNING", "\"WarningPercent\" > 0 AND \"WarningPercent\" < 100"));
        m.Entity<Student>().HasIndex(x => x.NormalizedName).IsUnique();
        m.Entity<Student>().Property(x => x.Username).HasMaxLength(40);
        m.Entity<Student>().Property(x => x.NormalizedName).HasMaxLength(40);
        m.Entity<Student>().Property(x => x.PasswordHash).HasMaxLength(512);
        m.Entity<Student>().Property(x => x.RecoveryHash).HasMaxLength(512);
        m.Entity<Student>().Property(x => x.SessionVersion).HasMaxLength(32).IsConcurrencyToken();
        m.Entity<Student>().Property(x => x.Failures).IsConcurrencyToken();
        m.Entity<Student>().Property(x => x.Revision).IsConcurrencyToken();
        m.Entity<Category>().ToTable("CATEGORIAS", t => t.HasCheckConstraint("CK_CATEGORY_KIND", "\"Kind\" IN (0,1)"));
        m.Entity<Category>().Property(x => x.Name).HasMaxLength(80);
        m.Entity<Category>().HasIndex(x => new { x.Kind, x.Name }).IsUnique();
        m.Entity<Category>().HasAlternateKey(x => new { x.Id, x.Kind });
        m.Entity<Entry>().ToTable("MOVIMIENTOS", t => {
            t.HasCheckConstraint("CK_ENTRY_AMOUNT", $"\"Amount\" > 0 AND \"Amount\" <= {amountLimit}");
            t.HasCheckConstraint("CK_ENTRY_KIND", "(\"Kind\" IN (0,1) AND \"CategoryId\" IS NOT NULL AND \"GoalId\" IS NULL) OR (\"Kind\" = 2 AND \"CategoryId\" IS NULL AND \"GoalId\" IS NOT NULL)");
        });
        m.Entity<Entry>().HasOne<Category>().WithMany().HasForeignKey(x => new { x.CategoryId, x.Kind }).HasPrincipalKey(x=>new{x.Id,x.Kind}).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Entry>().HasOne<Goal>().WithMany().HasForeignKey(x => new { x.GoalId, x.UserId }).HasPrincipalKey(x => new { x.Id, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Entry>().HasIndex(x => new { x.UserId, x.Date });
        m.Entity<Entry>().Property(x => x.Description).HasMaxLength(500).IsRequired(false);
        m.Entity<SpendingPlan>().ToTable("PRESUPUESTOS", t => t.HasCheckConstraint("CK_BUDGET_AMOUNT", $"\"Amount\" > 0 AND \"Amount\" <= {amountLimit}"));
        m.Entity<SpendingPlan>().Property(x => x.Month).HasMaxLength(7);
        m.Entity<SpendingPlan>().Property(x => x.Scope).HasMaxLength(80);
        m.Entity<SpendingPlan>().HasIndex(x => new { x.UserId, x.Month, x.Scope }).IsUnique();
        m.Entity<SpendingPlan>().HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Goal>().ToTable("METAS_AHORRO", t => t.HasCheckConstraint("CK_GOAL_TARGET", $"\"Target\" > 0 AND \"Target\" <= {amountLimit}"));
        m.Entity<Goal>().Property(x => x.Name).HasMaxLength(120);
        m.Entity<Goal>().Property(x => x.Description).HasMaxLength(500).IsRequired(false);
        m.Entity<Scenario>().ToTable("ESCENARIOS", t => {
            t.HasCheckConstraint("CK_SCENARIO_YEARS", "\"Years\" IN (1,3,5,10)");
            if(!Database.IsSqlite()) t.HasCheckConstraint("CK_SCENARIO_INPUTS", "\"Capital\" BETWEEN -1000000000 AND 1000000000 AND \"Income\" BETWEEN 0 AND 1000000000 AND \"Expense\" BETWEEN 0 AND 1000000000 AND \"IncomeChange\" BETWEEN -100 AND 100 AND \"ExpenseChange\" BETWEEN -100 AND 100 AND \"ContributionChange\" BETWEEN -100 AND 100 AND \"Inflation\" > -100 AND \"Inflation\" <= 100 AND \"AnnualReturn\" > -100 AND \"AnnualReturn\" <= 100 AND (\"Contribution\" IS NULL OR \"Contribution\" BETWEEN 0 AND 1000000000)");
        });
        m.Entity<Scenario>().Property(x => x.Name).HasMaxLength(100);
        ConfigureOwned<Entry>(m); ConfigureOwned<SpendingPlan>(m); ConfigureOwned<Goal>(m); ConfigureOwned<Scenario>(m);
        foreach (var entity in m.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties())
                if (p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)) { p.SetPrecision(28); p.SetScale(8); }
        m.Entity<Entry>().Property(x => x.Amount).HasPrecision(19,2);
        m.Entity<SpendingPlan>().Property(x => x.Amount).HasPrecision(19,2);
        m.Entity<Goal>().Property(x => x.Target).HasPrecision(19,2);
        if (Database.IsSqlite()) {
            m.Entity<Student>().Property(x => x.WarningPercent).HasConversion<int>();
            m.Entity<Entry>().Property(x => x.Amount).HasConversion(x => checked((long)(x * 100)), x => x / 100m);
            m.Entity<SpendingPlan>().Property(x => x.Amount).HasConversion(x => checked((long)(x * 100)), x => x / 100m);
            m.Entity<Goal>().Property(x => x.Target).HasConversion(x => checked((long)(x * 100)), x => x / 100m);
        } else {
            foreach(var p in m.Model.GetEntityTypes().SelectMany(x=>x.GetProperties()).Where(p=>p.ClrType==typeof(decimal)||p.ClrType==typeof(decimal?))) p.SetColumnType("NUMBER(28,8)");
            m.Entity<Entry>().Property(x=>x.Amount).HasColumnType("NUMBER(19,2)");
            m.Entity<SpendingPlan>().Property(x=>x.Amount).HasColumnType("NUMBER(19,2)");
            m.Entity<Goal>().Property(x=>x.Target).HasColumnType("NUMBER(19,2)");
        }
    }
    private static void ConfigureOwned<T>(ModelBuilder m) where T : OwnedRow
    {
        m.Entity<T>().HasBaseType((Type?)null);
        m.Entity<T>().HasKey(x => x.Id);
        m.Entity<T>().Property(x => x.RequestKey).HasMaxLength(32);
        m.Entity<T>().HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique();
        m.Entity<T>().HasOne<Student>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
