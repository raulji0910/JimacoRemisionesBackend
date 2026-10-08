using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface IJwtGenerador
{
    string GenerarToken(Usuario usuario);
}
