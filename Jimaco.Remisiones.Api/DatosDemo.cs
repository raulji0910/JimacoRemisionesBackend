using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Api;

// Datos de demostración para probar la app sin cargar datos reales. Solo se siembran si
// Seed:DatosDemo = true y las tablas están vacías. Los productos reales (las herramientas) NO salen de
// aquí: se cargan con scripts/cargar-herramientas.mjs.
public static class DatosDemo
{
    public static async Task SembrarAsync(AppDbContext db)
    {
        var ahora = DateTime.UtcNow;

        if (!await db.Productos.AnyAsync())
        {
            (string Codigo, string Nombre, string Unidad, decimal Precio, int Iva)[] productos =
            [
                ("DEMO-001", "Producto de demostración A", "Unidad", 10000, 19),
                ("DEMO-002", "Producto de demostración B", "Unidad", 25000, 19),
                ("DEMO-003", "Producto de demostración C", "Unidad", 120000, 19),
            ];

            foreach (var p in productos)
            {
                var producto = new Producto
                {
                    Codigo = p.Codigo,
                    Nombre = p.Nombre,
                    UnidadMedida = p.Unidad,
                    PrecioVenta = p.Precio,
                    Iva = p.Iva,
                    Activo = true,
                    FechaCreacion = ahora
                };
                db.Productos.Add(producto);
                Kardex.Registrar(db, producto, TipoMovimiento.CargaInicial, 50, ahora, "demo", motivo: "Stock inicial de demostración");
            }
        }

        if (!await db.Compradores.AnyAsync())
        {
            db.Compradores.Add(new Comprador
            {
                TipoPersona = TipoPersona.Natural,
                TipoDocumento = "CC",
                NumeroDocumento = "1015424228",
                PrimerNombre = "Camilo",
                SegundoNombre = "Andres",
                PrimerApellido = "Piragauta",
                SegundoApellido = "Paez",
                NombreCompleto = "Camilo Andres Piragauta Paez",
                Direccion = "CL 69 SUR 95A 41",
                Ciudad = "Bogota D.C.",
                Telefono = "32143137",
                Email = "demo.natural@example.com",
                FechaCreacion = ahora
            });

            db.Compradores.Add(new Comprador
            {
                TipoPersona = TipoPersona.Juridica,
                TipoDocumento = "NIT",
                NumeroDocumento = "900123456",
                Dv = DigitoVerificacion.Calcular("900123456"),
                PrimerNombre = "Constructora Demo S.A.S.",
                NombreCompleto = "Constructora Demo S.A.S.",
                Direccion = "Calle 100 # 15 - 20",
                Ciudad = "Bogota D.C.",
                Telefono = "6015550100",
                Email = "demo.juridica@example.com",
                FechaCreacion = ahora
            });
        }

        await db.SaveChangesAsync();
    }
}
