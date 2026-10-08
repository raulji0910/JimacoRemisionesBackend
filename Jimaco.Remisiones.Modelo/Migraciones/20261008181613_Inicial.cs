using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Remisiones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateSequence<int>(
                name: "SecuenciaConsecutivoRemision",
                schema: "dbo");

            migrationBuilder.CreateTable(
                name: "Compradores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TipoPersona = table.Column<int>(type: "int", nullable: false),
                    TipoDocumento = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NumeroDocumento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Dv = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PrimerNombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SegundoNombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PrimerApellido = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SegundoApellido = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NombreCompleto = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Ciudad = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Telefono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Origen = table.Column<int>(type: "int", nullable: false),
                    CodigoWorldOffice = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Compradores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Productos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PrecioVenta = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Costo = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Iva = table.Column<int>(type: "int", nullable: false),
                    Origen = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Productos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rol = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Remisiones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Consecutivo = table.Column<int>(type: "int", nullable: false),
                    CompradorId = table.Column<int>(type: "int", nullable: false),
                    CompradorTipoDocumentoSnapshot = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CompradorDocumentoSnapshot = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CompradorNombreSnapshot = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CompradorDireccionSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CompradorCiudadSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompradorTelefonoSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CompradorEmailSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Descuento = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Nota = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EstadoPago = table.Column<int>(type: "int", nullable: false),
                    FechaPago = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PagadaPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReferenciaPago = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TipoEntrega = table.Column<int>(type: "int", nullable: false),
                    EstadoEntrega = table.Column<int>(type: "int", nullable: false),
                    FechaEntrega = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorreoEnviadoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SincronizacionWo = table.Column<int>(type: "int", nullable: false),
                    SincronizadaWoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DocumentoWorldOffice = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ErrorSincronizacionWo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreadoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Remisiones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Remisiones_Compradores_CompradorId",
                        column: x => x.CompradorId,
                        principalTable: "Compradores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RemisionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RemisionId = table.Column<int>(type: "int", nullable: false),
                    ProductoId = table.Column<int>(type: "int", nullable: false),
                    ProductoCodigoSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProductoNombreSnapshot = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PrecioUnitario = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CostoSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Iva = table.Column<int>(type: "int", nullable: false),
                    Cantidad = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemisionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemisionItems_Remisiones_RemisionId",
                        column: x => x.RemisionId,
                        principalTable: "Remisiones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Compradores_NombreCompleto",
                table: "Compradores",
                column: "NombreCompleto");

            migrationBuilder.CreateIndex(
                name: "IX_Compradores_NumeroDocumento",
                table: "Compradores",
                column: "NumeroDocumento",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Productos_Codigo",
                table: "Productos",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_CompradorId",
                table: "Remisiones",
                column: "CompradorId");

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_Consecutivo",
                table: "Remisiones",
                column: "Consecutivo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_EstadoPago",
                table: "Remisiones",
                column: "EstadoPago");

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_SincronizacionWo",
                table: "Remisiones",
                column: "SincronizacionWo");

            migrationBuilder.CreateIndex(
                name: "IX_RemisionItems_RemisionId",
                table: "RemisionItems",
                column: "RemisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Productos");

            migrationBuilder.DropTable(
                name: "RemisionItems");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "Remisiones");

            migrationBuilder.DropTable(
                name: "Compradores");

            migrationBuilder.DropSequence(
                name: "SecuenciaConsecutivoRemision",
                schema: "dbo");
        }
    }
}
