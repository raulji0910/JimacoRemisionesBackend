using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Sincronizador;
using Microsoft.Extensions.Configuration;

// Sincronizador de Jimaco Remisiones: lee World Office (SOLO LECTURA) y empuja a la API de
// Remisiones por HTTPS saliente: productos, existencias por bodega y clientes. Pensado para correr
// como Tarea Programada de Windows dentro de la red de WO (cada pocos minutos): una corrida = un
// pase completo y salir. No escribe NADA en World Office (ver CLAUDE.md, "Fase 2").
//
// Opciones:  --reset  borra lo recordado (vuelve a enviar todo)
//            --solo=productos|existencias|compradores   corre solo esa parte

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Local.json", optional: true) // credenciales reales, nunca commiteado
    .AddEnvironmentVariables()
    .Build();

var cadenaWo = config["WorldOffice:ConnectionString"]
    ?? throw new InvalidOperationException("Falta configurar WorldOffice:ConnectionString.");
var apiUrl = config["Remisiones:ApiBaseUrl"]
    ?? throw new InvalidOperationException("Falta configurar Remisiones:ApiBaseUrl.");
var email = config["Remisiones:UsuarioServicio:Email"]
    ?? throw new InvalidOperationException("Falta configurar Remisiones:UsuarioServicio:Email.");
var password = config["Remisiones:UsuarioServicio:Password"]
    ?? throw new InvalidOperationException("Falta configurar Remisiones:UsuarioServicio:Password.");

var lote = int.TryParse(config["Sincronizador:Lote"], out var l) && l > 0 ? l : 1000;
var rutaEstado = Path.Combine(AppContext.BaseDirectory, config["Sincronizador:RutaEstado"] ?? "estado-sync.json");

var soloArg = args.FirstOrDefault(a => a.StartsWith("--solo=", StringComparison.OrdinalIgnoreCase))?["--solo=".Length..].ToLowerInvariant();
bool Corre(string parte) => soloArg is null || soloArg == parte;

if (args.Contains("--reset", StringComparer.OrdinalIgnoreCase) && File.Exists(rutaEstado))
{
    File.Delete(rutaEstado);
    Log("Estado borrado (--reset): se reenviará todo.");
}

var store = new EstadoStore(rutaEstado);
var estado = store.Leer();
var wo = new WorldOfficeReader(cadenaWo);

using var http = new HttpClient { BaseAddress = new Uri(apiUrl), Timeout = TimeSpan.FromMinutes(5) };
var api = new RemisionesApiClient(http, email, password);

try
{
    if (Corre("productos")) await SincronizarProductosAsync();
    if (Corre("existencias")) await SincronizarExistenciasAsync();
    if (Corre("compradores")) await SincronizarCompradoresAsync();
    Log("Listo.");
    return 0;
}
catch (Exception ex)
{
    Log($"ERROR: {ex.Message}");
    return 1;
}

// ---- Productos (maestro de Inventarios): incremental por rowversion ----
async Task SincronizarProductosAsync()
{
    var desde = EstadoStore.DesdeHex(estado.ProductosDesde);
    int total = 0, creados = 0, actualizados = 0, omitidos = 0;

    while (true)
    {
        var filas = await wo.ObtenerProductosAsync(desde, lote);
        if (filas.Count == 0) break;

        var resultado = await api.EnviarProductosAsync(filas.Select(Traductor.Producto).ToList());
        total += filas.Count; creados += resultado.Creados; actualizados += resultado.Actualizados; omitidos += resultado.Omitidos;
        foreach (var aviso in resultado.Avisos.Take(5)) Log($"  aviso: {aviso}");

        // Inclusive (>=): la última fila se reenvía en la corrida siguiente, es idempotente.
        desde = filas[^1].Version;
        estado.ProductosDesde = EstadoStore.Hex(desde);
        store.Guardar(estado);

        if (filas.Count < lote) break;
    }

    Log($"Productos: {total} leídos ({creados} nuevos, {actualizados} actualizados, {omitidos} omitidos).");
}

// ---- Existencias: se lee todo (agregado en la vista de WO) y se envía solo lo que cambió ----
async Task SincronizarExistenciasAsync()
{
    // La hora de la "foto" se toma ANTES de leer WO: así una venta registrada mientras se leía nunca se da
    // por incluida cuando quizá no lo está (lado seguro).
    var leidoEn = DateTime.UtcNow;
    var filas = await wo.ObtenerExistenciasAsync();

    var cambiadas = filas
        .Where(f => !estado.Existencias.TryGetValue(Clave(f), out var previa) || previa != f.Cantidad)
        .ToList();

    int creados = 0, actualizados = 0, omitidos = 0;
    var lotes = cambiadas.Chunk(lote).ToList();

    // Aunque no haya cambios se avisa la hora de la lectura: es el "latido" de la sincronización.
    if (lotes.Count == 0)
        await api.EnviarExistenciasAsync([], leidoEn);

    foreach (var grupo in lotes)
    {
        var resultado = await api.EnviarExistenciasAsync(
            grupo.Select(f => new SincExistenciaDto(f.IdWorldOffice, f.Bodega, f.Cantidad)).ToList(), leidoEn);
        creados += resultado.Creados; actualizados += resultado.Actualizados; omitidos += resultado.Omitidos;

        // Solo se da por enviado lo que la API ya aceptó.
        foreach (var f in grupo) estado.Existencias[Clave(f)] = f.Cantidad;
        store.Guardar(estado);
    }

    Log($"Existencias: {filas.Count} filas en WO, {cambiadas.Count} cambiaron ({creados} nuevas, {actualizados} actualizadas, {omitidos} de productos aún desconocidos).");
}

// ---- Clientes (terceros con propiedad Cliente): incremental por rowversion ----
async Task SincronizarCompradoresAsync()
{
    var desde = EstadoStore.DesdeHex(estado.CompradoresDesde);
    int total = 0, creados = 0, actualizados = 0, omitidos = 0;

    while (true)
    {
        var filas = await wo.ObtenerCompradoresAsync(desde, lote);
        if (filas.Count == 0) break;

        var dtos = new List<SincCompradorDto>();
        foreach (var fila in filas)
        {
            var dto = Traductor.Comprador(fila, out var motivo);
            if (dto is not null) dtos.Add(dto);
            else { omitidos++; Log($"  omitido: {motivo}"); }
        }

        if (dtos.Count > 0)
        {
            var resultado = await api.EnviarCompradoresAsync(dtos);
            creados += resultado.Creados; actualizados += resultado.Actualizados; omitidos += resultado.Omitidos;
            foreach (var aviso in resultado.Avisos.Take(5)) Log($"  aviso: {aviso}");
        }

        total += filas.Count;
        desde = filas[^1].Version;
        estado.CompradoresDesde = EstadoStore.Hex(desde);
        store.Guardar(estado);

        if (filas.Count < lote) break;
    }

    Log($"Clientes: {total} leídos ({creados} nuevos, {actualizados} actualizados, {omitidos} omitidos).");
}

static string Clave(ExistenciaWoRow f) => $"{f.IdWorldOffice}|{f.Bodega}";

static void Log(string mensaje) => Console.WriteLine($"[{DateTime.Now:s}] {mensaje}");
