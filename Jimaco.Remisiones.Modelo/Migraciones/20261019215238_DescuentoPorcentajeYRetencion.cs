using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Remisiones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class DescuentoPorcentajeYRetencion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DescuentoPorcentaje",
                table: "Remisiones",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Retencion",
                table: "Remisiones",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescuentoPorcentaje",
                table: "Remisiones");

            migrationBuilder.DropColumn(
                name: "Retencion",
                table: "Remisiones");
        }
    }
}
