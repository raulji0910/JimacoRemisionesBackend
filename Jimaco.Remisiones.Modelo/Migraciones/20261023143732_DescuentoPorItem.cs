using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Remisiones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class DescuentoPorItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DescuentoPorcentaje",
                table: "RemisionItems",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            // Un descuento general en % equivale a ese mismo % en cada línea (el IVA se calculaba sobre la base descontada de cada tramo):
            // se copia a los ítems antes de borrar las columnas de la remisión, para no perder descuentos ya registrados.
            migrationBuilder.Sql(
                "UPDATE i SET i.DescuentoPorcentaje = r.DescuentoPorcentaje FROM RemisionItems i JOIN Remisiones r ON r.Id = i.RemisionId WHERE r.DescuentoPorcentaje > 0");

            migrationBuilder.DropColumn(
                name: "Descuento",
                table: "Remisiones");

            migrationBuilder.DropColumn(
                name: "DescuentoPorcentaje",
                table: "Remisiones");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescuentoPorcentaje",
                table: "RemisionItems");

            migrationBuilder.AddColumn<decimal>(
                name: "Descuento",
                table: "Remisiones",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DescuentoPorcentaje",
                table: "Remisiones",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
