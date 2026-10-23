using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

public record ReglasRetencionesDto(decimal ReteFuentePorcentaje, decimal ReteIcaPorMil, decimal ReteIvaPorcentaje, decimal Uvt, decimal BaseMinima);

// Tarifas fijas de las retenciones y valor mínimo (antes de IVA) para aplicarlas; el formulario las lee de aquí para no duplicarlas.
[ApiController]
[Authorize]
[Route("api/configuracion")]
public class ConfiguracionController(IConfiguration configuracion) : ControllerBase
{
    [HttpGet("retenciones")]
    public ActionResult<ReglasRetencionesDto> Retenciones()
    {
        var r = ReglasRetenciones.Cargar(configuracion);
        return Ok(new ReglasRetencionesDto(r.ReteFuentePorcentaje, r.ReteIcaPorMil, r.ReteIvaPorcentaje, r.Uvt, r.BaseMinima));
    }
}
