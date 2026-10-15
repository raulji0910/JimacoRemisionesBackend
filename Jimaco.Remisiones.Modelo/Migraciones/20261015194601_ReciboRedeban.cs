using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Remisiones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class ReciboRedeban : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReferenciaPago",
                table: "Remisiones",
                newName: "ReciboRedeban");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReciboRedeban",
                table: "Remisiones",
                newName: "ReferenciaPago");
        }
    }
}
