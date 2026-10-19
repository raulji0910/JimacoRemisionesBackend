namespace Jimaco.Remisiones.Negocio.Interfaces;

/// <summary>Cola en memoria para enviar el correo de una remisión pagada sin hacer esperar al cajero.</summary>
public interface ICorreoRemisionCola
{
    void Encolar(int remisionId);
}
