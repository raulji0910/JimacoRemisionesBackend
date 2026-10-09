using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Remisiones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarIntegracionWorldOffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExistenciaActualizadaEn",
                table: "Productos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdWorldOffice",
                table: "Productos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductoExistencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductoId = table.Column<int>(type: "int", nullable: false),
                    Bodega = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoExistencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductoExistencias_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Productos_IdWorldOffice",
                table: "Productos",
                column: "IdWorldOffice",
                unique: true,
                filter: "[IdWorldOffice] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Compradores_CodigoWorldOffice",
                table: "Compradores",
                column: "CodigoWorldOffice");

            migrationBuilder.CreateIndex(
                name: "IX_ProductoExistencias_ProductoId_Bodega",
                table: "ProductoExistencias",
                columns: new[] { "ProductoId", "Bodega" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductoExistencias");

            migrationBuilder.DropIndex(
                name: "IX_Productos_IdWorldOffice",
                table: "Productos");

            migrationBuilder.DropIndex(
                name: "IX_Compradores_CodigoWorldOffice",
                table: "Compradores");

            migrationBuilder.DropColumn(
                name: "ExistenciaActualizadaEn",
                table: "Productos");

            migrationBuilder.DropColumn(
                name: "IdWorldOffice",
                table: "Productos");
        }
    }
}
