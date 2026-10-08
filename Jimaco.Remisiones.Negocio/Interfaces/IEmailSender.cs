namespace Jimaco.Remisiones.Negocio.Interfaces;

public record AdjuntoCorreo(string NombreArchivo, byte[] Contenido, string? ContentType);

/// <summary>Abstrae el envío de correo para no acoplar el servicio de remisiones a un proveedor concreto.</summary>
public interface IEmailSender
{
    Task EnviarAsync(string destinatario, string asunto, string cuerpoHtml, IReadOnlyList<AdjuntoCorreo>? adjuntos = null, CancellationToken ct = default);
}
