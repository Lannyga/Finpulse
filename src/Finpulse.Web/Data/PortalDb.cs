using Oracle.ManagedDataAccess.Client;
using Finpulse.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Finpulse.Web.Data;

public sealed class PortalDb(DbContextOptions<PortalDb> options) : DbContext(options)
{
    public DbSet<Student> Users => Set<Student>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<Goal> Goals => Set<Goal>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Student>().ToTable("USUARIOS");
        m.Entity<Student>().HasIndex(x => x.NormalizedName).IsUnique();
        m.Entity<Student>().Property(x => x.Username).HasMaxLength(40);
        m.Entity<Student>().Property(x => x.NormalizedName).HasMaxLength(40);
        m.Entity<Student>().Property(x => x.PasswordHash).HasMaxLength(512);
        m.Entity<Student>().Property(x => x.RecoveryHash).HasMaxLength(512);
        m.Entity<Student>().Property(x => x.WarningPercent).HasColumnType("NUMBER(28,8)");
        m.Entity<Student>().Property(x => x.SessionVersion).HasMaxLength(32).IsConcurrencyToken();
        m.Entity<Student>().Property(x => x.Failures).IsConcurrencyToken();
        m.Entity<Student>().Property(x => x.Revision).IsConcurrencyToken();

        m.Entity<Category>().ToTable("CATEGORIAS");
        m.Entity<Category>().Property(x => x.Name).HasMaxLength(80);
        m.Entity<Category>().HasIndex(x => new { x.Kind, x.Name }).IsUnique();
        m.Entity<Category>().HasAlternateKey(x => new { x.Id, x.Kind });

        ConfigureOwned<Entry>(m, "MOVIMIENTOS");
        m.Entity<Entry>().Property(x => x.Amount).HasColumnType("NUMBER(19,2)");
        m.Entity<Entry>().Property(x => x.Description).HasMaxLength(500).IsRequired(false);
        m.Entity<Entry>().HasOne<Category>().WithMany().HasForeignKey(x => new { x.CategoryId, x.Kind })
            .HasPrincipalKey(x => new { x.Id, x.Kind }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Entry>().HasOne<Goal>().WithMany().HasForeignKey(x => new { x.GoalId, x.UserId })
            .HasPrincipalKey(x => new { x.Id, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Entry>().HasIndex(x => new { x.UserId, x.Date });

        ConfigureOwned<Goal>(m, "METAS_AHORRO");
        m.Entity<Goal>().Property(x => x.Name).HasMaxLength(120);
        m.Entity<Goal>().Property(x => x.Description).HasMaxLength(500).IsRequired(false);
        m.Entity<Goal>().Property(x => x.Target).HasColumnType("NUMBER(19,2)");
    }

    private static void ConfigureOwned<T>(ModelBuilder m, string table) where T : OwnedRow
    {
        m.Entity<T>().HasBaseType((Type?)null).ToTable(table);
        m.Entity<T>().HasKey(x => x.Id);
        m.Entity<T>().Property(x => x.RequestKey).HasMaxLength(32);
        m.Entity<T>().HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique();
        m.Entity<T>().HasOne<Student>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }

    public async Task Verify(string expectedUser)
    {
        await Database.OpenConnectionAsync();
        try {
            using var command = ((OracleConnection)Database.GetDbConnection()).CreateCommand();
            command.CommandText = "SELECT USER, SYS_CONTEXT('USERENV', 'CON_NAME') FROM DUAL";
            using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            if (reader.GetString(0) != expectedUser || reader.GetString(1) != "FREEPDB1")
                throw new InvalidOperationException("Usuario o contenedor Oracle distinto del esperado.");
            await reader.CloseAsync();
            // Validate the four tables used by the portal without modifying the schema.
            _ = await Users.AsNoTracking().OrderBy(x => x.Id).Take(1).ToArrayAsync();
            _ = await Categories.AsNoTracking().OrderBy(x => x.Id).Take(1).ToArrayAsync();
            _ = await Entries.AsNoTracking().OrderBy(x => x.Id).Take(1).ToArrayAsync();
            _ = await Goals.AsNoTracking().OrderBy(x => x.Id).Take(1).ToArrayAsync();
            Console.WriteLine($"Oracle conectado: {expectedUser}. Cuatro tablas verificadas.");
        } finally { await Database.CloseConnectionAsync(); }
    }
}
