using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface IRemisionPdfBuilder
{
    byte[] Generar(RemisionDetalleDto remision);
}
