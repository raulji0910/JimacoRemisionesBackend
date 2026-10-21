using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Remisiones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class MedioPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MedioPago",
                table: "Remisiones",
                type: "int",
                nullable: true);

            // Todo lo cobrado hasta hoy fue por datáfono.
            migrationBuilder.Sql("UPDATE Remisiones SET MedioPago = 1 WHERE FechaPago IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MedioPago",
                table: "Remisiones");
        }
    }
}
