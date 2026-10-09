using Jimaco.Remisiones.Modelo.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Modelo;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Comprador> Compradores => Set<Comprador>();
    public DbSet<Remision> Remisiones => Set<Remision>();
    public DbSet<RemisionItem> RemisionItems => Set<RemisionItem>();
    public DbSet<ProductoExistencia> ProductoExistencias => Set<ProductoExistencia>();

    // Consumida vía "SELECT NEXT VALUE FOR dbo.SecuenciaConsecutivoRemision" para asignar el
    // número oficial de la remisión de forma atómica ante concurrencia.
    public const string SecuenciaConsecutivoRemision = "SecuenciaConsecutivoRemision";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Producto>(e =>
        {
            e.HasIndex(p => p.Codigo).IsUnique();
            e.HasIndex(p => p.IdWorldOffice).IsUnique().HasFilter("[IdWorldOffice] IS NOT NULL");
        });

        modelBuilder.Entity<ProductoExistencia>(e =>
        {
            e.HasOne(x => x.Producto)
                .WithMany(p => p.Existencias)
                .HasForeignKey(x => x.ProductoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ProductoId, x.Bodega }).IsUnique();
        });

        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Comprador>(e =>
        {
            // El mismo número de documento no se repite: es la llave para autocompletar al volver.
            e.HasIndex(c => c.NumeroDocumento).IsUnique();
            e.HasIndex(c => c.NombreCompleto);
            e.HasIndex(c => c.CodigoWorldOffice);
        });

        modelBuilder.Entity<Remision>(e =>
        {
            e.HasOne(r => r.Comprador)
                .WithMany()
                .HasForeignKey(r => r.CompradorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(r => r.Consecutivo).IsUnique();
            e.HasIndex(r => r.EstadoPago);
            e.HasIndex(r => r.SincronizacionWo);
        });

        modelBuilder.Entity<RemisionItem>(e =>
        {
            e.HasOne(i => i.Remision)
                .WithMany(r => r.Items)
                .HasForeignKey(i => i.RemisionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.HasSequence<int>(SecuenciaConsecutivoRemision, schema: "dbo")
            .StartsAt(1)
            .IncrementsBy(1);
    }
}
