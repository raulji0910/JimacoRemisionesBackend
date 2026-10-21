using Jimaco.Remisiones.Negocio.Interfaces;
using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.Extensions.DependencyInjection;

namespace Jimaco.Remisiones.Negocio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNegocio(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IProductoService, ProductoService>();
        services.AddScoped<ICompradorService, CompradorService>();
        services.AddScoped<IRemisionService, RemisionService>();
        services.AddScoped<IInventarioService, InventarioService>();
        services.AddScoped<ICierreCajaService, CierreCajaService>();
        services.AddScoped<IJwtGenerador, JwtGenerador>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IRemisionPdfBuilder, Documentos.RemisionPdfBuilder>();
        services.AddSingleton<CorreoRemisionCola>();
        services.AddSingleton<ICorreoRemisionCola>(sp => sp.GetRequiredService<CorreoRemisionCola>());
        services.AddHostedService(sp => sp.GetRequiredService<CorreoRemisionCola>());
        return services;
    }
}
