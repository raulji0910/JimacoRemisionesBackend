namespace Jimaco.Remisiones.Modelo.Entidades;

// Una sola fila (Id = 1) con el estado global de la integración con World Office.
public class EstadoIntegracionWo
{
    public int Id { get; set; }

    // Instante (UTC) en que se leyó la última "foto" completa de existencias de WO. Se actualiza en
    // cada corrida del Sincronizador aunque no haya cambios (es su latido), y en cada importación
    // manual. Sirve para saber qué ventas de esta app ya quedaron incluidas en esa foto y cuáles
    // todavía no (ver ProductoService: Reservado / Disponible).
    public DateTime? UltimaFotoExistenciasEn { get; set; }
}
