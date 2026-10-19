using System.Threading.Channels;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jimaco.Remisiones.Negocio.Servicios;

/// <summary>
/// El pago responde al instante y el correo (PDF + SMTP al cliente y a contabilidad, ~segundos) sale aquí, en segundo plano.
/// La cola vive en memoria: si la API se reinicia justo en esos segundos el correo no sale, pero la remisión queda
/// "sin enviar" y se reintenta con el botón del detalle.
/// </summary>
public class CorreoRemisionCola(IServiceScopeFactory scopeFactory, ILogger<CorreoRemisionCola> logger)
    : BackgroundService, ICorreoRemisionCola
{
    private readonly Channel<int> canal = Channel.CreateUnbounded<int>();

    public void Encolar(int remisionId) => canal.Writer.TryWrite(remisionId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in canal.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var remisiones = scope.ServiceProvider.GetRequiredService<IRemisionService>();
                var resultado = await remisiones.EnviarCorreoAsync(id, stoppingToken);
                if (resultado is { CorreoEnviado: false })
                    logger.LogWarning("Correo de la remisión {Id} no enviado: {Error}", id, resultado.ErrorCorreo);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo enviando en segundo plano el correo de la remisión {Id}", id);
            }
        }
    }
}
