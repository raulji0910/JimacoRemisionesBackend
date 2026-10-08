using Jimaco.Remisiones.Negocio.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace Jimaco.Remisiones.Negocio.Servicios;

/// <summary>
/// Envía correo vía SMTP con la configuración <c>Smtp:*</c> (appsettings / variables de entorno).
/// <c>Smtp:UseSsl = true</c> asume SSL directo (típicamente 465); en <c>false</c>, STARTTLS (587).
/// Para producción usar un proveedor transaccional (Brevo/SendGrid) y no una cuenta personal de
/// Gmail: Google inhabilita las cuentas usadas para enviar correo automatizado desde la nube.
/// </summary>
public class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task EnviarAsync(string destinatario, string asunto, string cuerpoHtml, IReadOnlyList<AdjuntoCorreo>? adjuntos = null, CancellationToken ct = default)
    {
        var host = configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("El envío de correo no está configurado (falta Smtp:Host).");

        var puerto = int.TryParse(configuration["Smtp:Port"], out var p) ? p : 587;
        var usarSsl = bool.TryParse(configuration["Smtp:UseSsl"], out var ssl) && ssl;
        var usuario = configuration["Smtp:Usuario"] ?? throw new InvalidOperationException("Falta configurar Smtp:Usuario.");
        var password = configuration["Smtp:Password"] ?? throw new InvalidOperationException("Falta configurar Smtp:Password.");
        var remitenteNombre = configuration["Smtp:RemitenteNombre"] ?? "Jimaco";
        var remitenteEmail = configuration["Smtp:RemitenteEmail"] ?? usuario;

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(remitenteNombre, remitenteEmail));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = asunto;

        var cuerpo = new BodyBuilder { HtmlBody = cuerpoHtml };
        foreach (var adjunto in adjuntos ?? [])
            cuerpo.Attachments.Add(adjunto.NombreArchivo, adjunto.Contenido, ContentType.Parse(adjunto.ContentType ?? "application/octet-stream"));
        mensaje.Body = cuerpo.ToMessageBody();

        using var cliente = new SmtpClient();
        var modoSeguridad = usarSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        await cliente.ConnectAsync(host, puerto, modoSeguridad, ct);
        await cliente.AuthenticateAsync(usuario, password, ct);
        await cliente.SendAsync(mensaje, ct);
        await cliente.DisconnectAsync(true, ct);
    }
}
