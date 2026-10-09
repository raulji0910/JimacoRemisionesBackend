namespace Jimaco.Remisiones.Negocio.Servicios;

// Configuración de la integración con World Office (sección "WorldOffice" de appsettings).
public class WorldOfficeOptions
{
    public const string SeccionConfiguracion = "WorldOffice";

    // Bodega de WO desde la que se vende en el punto de venta; su existencia es la que muestra la
    // app. WO tiene 4 bodegas (códigos de texto, ej. "Uno" = "Principal").
    public string BodegaVenta { get; set; } = "Uno";
}
