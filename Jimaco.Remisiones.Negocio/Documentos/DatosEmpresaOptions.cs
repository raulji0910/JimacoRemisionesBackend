namespace Jimaco.Remisiones.Negocio.Documentos;

// Datos de la empresa que se muestran en el encabezado y pie del PDF de la remisión. Configurables
// vía appsettings.json / variables de entorno (sección "DatosEmpresa"), no hardcodeados.
public class DatosEmpresaOptions
{
    public const string SeccionConfiguracion = "DatosEmpresa";

    public string Nombre { get; set; } = "JIMACO";
    public string? Nit { get; set; }
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Ciudad { get; set; }
    public string? Email { get; set; }
    public string? SitioWeb { get; set; }
}
