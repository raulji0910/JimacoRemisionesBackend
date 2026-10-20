using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

public record MunicipioDto(string Codigo, string Nombre, string Departamento, string Etiqueta);

// Listado oficial de municipios del DANE (Divipola) para el desplegable de ciudad del comprador.
[ApiController]
[Authorize]
[Route("api/municipios")]
public class MunicipiosController : ControllerBase
{
    [HttpGet]
    [ResponseCache(Duration = 86400)]
    public ActionResult<IEnumerable<MunicipioDto>> Listar()
        => Ok(MunicipiosDane.Todos.Select(m => new MunicipioDto(m.Codigo, m.Nombre, m.Departamento, m.Etiqueta)));
}
