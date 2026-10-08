using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Api;

// Datos de prueba para poder probar la app sin la carga real de World Office. Solo se siembran si
// Seed:DatosDemo = true y las tablas están vacías. Los códigos son inventados, NO son los de WO.
public static class DatosDemo
{
    public static async Task SembrarAsync(AppDbContext db)
    {
        var ahora = DateTime.UtcNow;

        if (!await db.Productos.AnyAsync())
        {
            (string Codigo, string Nombre, string Unidad, decimal Precio, decimal Costo, int Iva)[] productos =
            [
                ("D-1001", "Cemento gris uso general x 50 kg", "BULTO", 31500, 26800, 19),
                ("D-1002", "Varilla corrugada 3/8 x 6 m", "UND", 18900, 15600, 19),
                ("D-1003", "Tornillo drywall 6 x 1 (caja x 100)", "CAJA", 8200, 5900, 19),
                ("D-1004", "Pintura vinilo tipo 1 blanco galón", "GALON", 62000, 47500, 19),
                ("D-1005", "Cinta de enmascarar 18 mm x 40 m", "UND", 3800, 2400, 19),
                ("D-1006", "Taladro percutor 1/2\" 650 W", "UND", 189000, 141000, 19),
                ("D-1007", "Guante de carnaza par", "PAR", 9500, 6100, 19),
                ("D-1008", "Arena lavada m3", "M3", 78000, 61000, 0),
                ("D-1009", "Tubo PVC presión 1/2\" x 6 m", "UND", 14200, 10300, 5),
                ("D-1010", "Lámina drywall 1/2\" 1.22 x 2.44 m", "UND", 29900, 23100, 19),
            ];

            foreach (var p in productos)
                db.Productos.Add(new Producto
                {
                    Codigo = p.Codigo,
                    Nombre = p.Nombre,
                    UnidadMedida = p.Unidad,
                    PrecioVenta = p.Precio,
                    Costo = p.Costo,
                    Iva = p.Iva,
                    Origen = OrigenRegistro.Local,
                    Activo = true,
                    FechaCreacion = ahora
                });
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
                Origen = OrigenRegistro.WorldOffice,
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
                Origen = OrigenRegistro.Local,
                FechaCreacion = ahora
            });
        }

        await db.SaveChangesAsync();
    }
}
