namespace Jimaco.Remisiones.Negocio.Interfaces;

/// <summary>Cola en memoria para enviar el correo de una remisión pagada sin hacer esperar al cajero.</summary>
public interface ICorreoRemisionCola
{
    // soloCompras: solo la copia a compras (cuando una remisión ya pagada pasa a envío posterior).
    void Encolar(int remisionId, bool soloCompras = false);
}
