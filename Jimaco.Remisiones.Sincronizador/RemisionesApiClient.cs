using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Sincronizador;

/// <summary>
/// Cliente HTTP contra la API de Jimaco Remisiones. Entra por la puerta de siempre (login con un
/// usuario de servicio de rol Sincronizador), sin atajos: la API aplica sus propias reglas.
/// </summary>
public class RemisionesApiClient(HttpClient http, string email, string password)
{
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private string? _token;
    private DateTimeOffset _tokenExpira = DateTimeOffset.MinValue;

    public Task<SincResultadoDto> EnviarProductosAsync(IReadOnlyList<SincProductoDto> items, CancellationToken ct = default)
        => PostAsync("api/sincronizacion/productos", new SincLoteProductosDto(items), ct);

    public Task<SincResultadoDto> EnviarExistenciasAsync(IReadOnlyList<SincExistenciaDto> items, CancellationToken ct = default)
        => PostAsync("api/sincronizacion/existencias", new SincLoteExistenciasDto(items), ct);

    public Task<SincResultadoDto> EnviarCompradoresAsync(IReadOnlyList<SincCompradorDto> items, CancellationToken ct = default)
        => PostAsync("api/sincronizacion/compradores", new SincLoteCompradoresDto(items), ct);

    private async Task<SincResultadoDto> PostAsync<T>(string ruta, T cuerpo, CancellationToken ct)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, ruta)
        {
            Content = JsonContent.Create(cuerpo, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detalle = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"La API respondió {(int)response.StatusCode} en {ruta}: {Truncar(detalle, 500)}");
        }

        return (await response.Content.ReadFromJsonAsync<SincResultadoDto>(JsonOpciones, ct))!;
    }

    private async Task AsegurarTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpira)
            return;

        using var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequestDto(email, password), JsonOpciones, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"No se pudo iniciar sesión en la API ({(int)response.StatusCode}). Revisa Remisiones:UsuarioServicio.");

        var login = (await response.Content.ReadFromJsonAsync<LoginResponseDto>(JsonOpciones, ct))!;
        _token = login.Token;
        // El token dura 8 horas por defecto; se renueva con mucho margen para una corrida corta.
        _tokenExpira = DateTimeOffset.UtcNow.AddMinutes(30);
    }

    private static string Truncar(string texto, int max) => texto.Length <= max ? texto : texto[..max];
}
