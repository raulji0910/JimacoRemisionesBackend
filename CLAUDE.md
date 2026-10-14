# CLAUDE.md

Guía para Claude Code (claude.ai/code) en este repositorio.

## Qué es

**Jimaco Remisiones**: app de **venta directa en el punto de venta**. Se arma una **remisión** (no una cotización), se cobra con un **datáfono Redeban** (hoy de forma manual, sin integración) y se marca como pagada; al marcarla se envía la remisión en PDF al correo del comprador. También registra si lo vendido se **entregó en sitio** o requiere **envío posterior**.

Nació clonando [Ferrealiados Cotizaciones](../Ferrealiados.Cotizaciones) (mismo stack y patrones: .NET 10, capas Modelo/Negocio/Api, QuestPDF, JWT) con la marca de Jimaco (verde + logo). Base de datos, despliegue y datos son independientes de los demás proyectos.

El frontend vive en el repo hermano **`Jimaco.Remisiones.Web`** (`../Jimaco.Remisiones.Web`); `docker-compose.yml` lo construye desde esa ruta relativa, así que ambos repos deben estar clonados lado a lado.

## Dominio

- **Producto**: `Codigo` (el de World Office, obligatorio y único), un solo `PrecioVenta` fijo **sin IVA**, `Costo` (solo margen interno, nunca en el PDF), `Iva` (0/5/19), `Origen` (Local | WorldOffice). Sin proveedores ni comparación de precios.
- **Comprador**: replica el "tercero" de World Office (tipo/número/DV, nombres separados, dirección, ciudad, correo). Se crea en el momento de la venta y se reutiliza buscando por documento (`GET /api/compradores/por-documento/{n}`). El DV del NIT se calcula (`DigitoVerificacion`). El correo es obligatorio (ahí se envía la remisión).
- **Remisión**: consecutivo oficial `RM-00001` desde una SQL SEQUENCE (`SecuenciaConsecutivoRemision`; se lee con ADO.NET directo porque EF envuelve `NEXT VALUE FOR` en una subconsulta y SQL Server lo rechaza, error 11719). Guarda **snapshot** del comprador y de cada ítem (código, nombre, precio, IVA, costo) para que no cambie si el catálogo se edita.
- **IVA y descuento**: igual que el cotizador de Ferrealiados — el descuento global en pesos se prorratea entre las tarifas de IVA según la participación de cada tramo (`RemisionCalculos`, duplicado en el front en `core/utils/calculos-remision.ts` solo para mostrar el total en vivo; el oficial siempre lo calcula el backend).

### Ciclo de vida

- `EstadoPago`: Pendiente → Pagada (o Anulada). Solo se **edita** mientras está Pendiente.
- Al marcar **Pagada** (`POST /{id}/pagar`, con referencia/voucher opcional): si la entrega es `EnSitio` queda `Entregada` de una vez; si es `Envio` queda `Pendiente` hasta `POST /{id}/entregar`. Luego intenta enviar el correo con el PDF: **un fallo del correo nunca revierte el pago**, se informa en la respuesta (`correoEnviado`/`errorCorreo`) y se reintenta con `POST /{id}/enviar-correo`.
- **Anular** exige motivo. Una remisión Pagada solo la anula un **Admin**. Se bloquea si ya descontó inventario en World Office (la reversa se hace allá).
- Concurrencia optimista con `RowVersion` (409 si dos personas tocan la misma remisión).

### Roles
`Admin` (gestiona usuarios, anula remisiones pagadas, elimina productos) y `Operador` (todo lo demás). Por ahora un solo rol operativo; se separará (cajero / admin / logística) más adelante.

## Integración con World Office (Fase 2)

Hallazgos completos de la exploración de la base de WO (esquema, vista de existencias, anatomía de una `REM`, triggers, contabilización): `ProyectosJimaco/RemisionesWO/hallazgos-worldoffice.md` — **leer antes de tocar esta parte**. Preguntas para soporte de WO: `RemisionesWO/preguntas-soporte-worldoffice.md`.

### 2a — Lectura WO → app: CONSTRUIDA y probada (contra una base simulada, no contra WO real)
`Jimaco.Remisiones.Sincronizador` (consola .NET, mismo patrón que `Jimaco.Aprobaciones.Sincronizador`): lee WO **solo lectura** y empuja por HTTPS saliente a `POST /api/sincronizacion/{productos|existencias|compradores}` autenticándose con un usuario de **rol `Sincronizador`** (se crea en la pantalla Usuarios). No escribe nada en WO.
- **Productos** (`Inventarios`): incremental por `VersionCol` (rowversion, inclusive). Trae código, nombre, unidad, IVA (WO lo guarda como fracción 0.19 → se convierte a 19; `Activo = -1` es "sí"). **No trae precio ni costo**: WO casi no tiene precios en el catálogo (se digitan en cada documento). Un producto nuevo de WO nace **sin precio** (`PrecioVenta = 0`) y **no se puede vender** hasta asignarle uno en Productos; la sincronización nunca pisa precio ni costo.
- **Existencias**: se lee la vista de WO `Vista_ExistenciasPorBodegas` (Σ Cantidad × EfectoSobreElInventario), se compara con lo ya enviado (archivo de estado) y solo se envía lo que cambió. La app muestra la de la bodega de venta (`WorldOffice:BodegaVenta`, por defecto `"Uno"` = Principal); "—" = nunca sincronizada. La venta no se bloquea por falta de stock, solo avisa.
- **Clientes** (`Terceros` con `Propiedades LIKE '%Cliente%'` — el formato del texto es inconsistente: `Cliente;`, `Cliente`, `Cliente;Proveedor;`…): incremental por la mayor `VersionCol` entre el tercero y su dirección principal. Un NIT sin apellidos = persona jurídica. Tipos de documento: se traducen de `IdTipoIdentificacion` (= código DIAN) a los códigos de la app; "Consumidor final" se omite. **WO manda en todo salvo el correo**: el correo de WO solo completa el del comprador cuando estaba vacío (el digitado en caja no se pisa). Un cliente de WO sin correo no se puede usar en una remisión hasta editarlo y agregarlo. Un comprador local con el mismo documento se **enlaza** al tercero de WO (no se duplica).
- Config (`appsettings.json` / `appsettings.Local.json` / variables de entorno): `WorldOffice:ConnectionString` (login de solo lectura), `Remisiones:ApiBaseUrl`, `Remisiones:UsuarioServicio:{Email,Password}`, `Sincronizador:Lote` (1000), `Sincronizador:RutaEstado`. Opciones de línea de comandos: `--reset` (reenviar todo), `--solo=productos|existencias|compradores`.
- Despliegue previsto: copia publicada en el servidor/PC de la red de WO + Tarea Programada de Windows cada pocos minutos, "no iniciar una nueva instancia" (igual que Aprobaciones). **Aún no desplegado ni probado contra la base real de WO.**
- **Probar sin tocar WO**: `Jimaco.Remisiones.Sincronizador/pruebas/wo-mock.sql` crea una base `WoMock` con las mismas tablas/columnas reales (y la misma definición de la vista) dentro del contenedor SQL de Remisiones; correr el Sincronizador con `WorldOffice__ConnectionString=Server=127.0.0.1,1436;Database=WoMock;...` y `Remisiones__*` por variables de entorno. Usar `127.0.0.1`, no `localhost` (resuelve a IPv6 y expira).

### Inventario en la app: espejo de WO + "reservado" (decisión del usuario, 2026-10-14)
World Office sigue siendo la verdad del stock; la app guarda una **foto** (`ProductoExistencia`, bodega de venta) con la hora en que se leyó (`EstadoIntegracionWo.UltimaFotoExistenciasEn`, una sola fila). Como la base de WO es local y la sincronización es asíncrona, lo que se vende desde la app no aparece en WO hasta que alguien lo registra allá (o, en la Fase 2b, hasta que la app lo escriba). Para no vender dos veces lo mismo:

```
Disponible = Existencia WO (la foto)  −  Reservado
Reservado  = unidades de remisiones PAGADAS en la app que WO aún no refleja
```
Una remisión pagada **deja de reservar** cuando está marcada como registrada en WO (`SincronizacionWo = Sincronizada`) **y** `SincronizadaWoEn <= UltimaFotoExistenciasEn` (la foto es posterior y ya la incluye). Mientras la foto sea más vieja, o la remisión no esté registrada, sigue restando. Si la sincronización se detiene la foto envejece y la reserva se queda (lado seguro). Lógica en `ProductoService.ConReservadoAsync`; `Disponible` es `null` si el producto nunca se sincronizó. Pendientes/anuladas no reservan. La venta **no se bloquea** por falta de stock, solo avisa (ver Frontend).
- **Carga inicial / refresco manual** (sin desplegar nada en la red de WO): pantalla `Productos → Importar inventario` (solo Admin). Muestra una consulta SSMS de solo lectura (existencia de la bodega "Uno" + último precio de venta en REM/FV, números como texto para evitar problemas de separador decimal) **filtrada a ítems de clase Producto o Servicio que se hayan vendido alguna vez o tengan existencia** (≈14.400 de 29.786; WO mezcla con los productos conceptos de gasto, activos fijos y catálogo sin movimiento — ver RemisionesWO/hallazgos-worldoffice.md §12; los servicios van sin existencia), se guarda como CSV y se sube; el frontend lo parte en lotes de 500 y llama `POST /api/inventario/importar`. La hora de la foto es la de la exportación (se propone la de modificación del archivo). Idempotente; nunca borra productos; el precio solo se asigna a productos sin precio salvo que se marque "reemplazar precios". `GET /api/inventario/estado` da la hora de la foto y cuántos productos activos siguen sin precio.
- **Registrar en WO a mano** (mientras no escribamos en WO): botón en el detalle de una remisión pagada → `POST /api/remisiones/{id}/registrada-en-wo {documento}` (ej. "REM 29301"). Propuesta pendiente: que quien la digite en WO escriba nuestro número `RM-xxxxx` en un campo libre de la REM para que el Sincronizador la reconozca solo.
- El **Sincronizador** manda `LeidoEn` (hora UTC tomada *antes* de leer WO) en cada corrida de existencias, aunque no haya cambios: es el latido que mueve la foto.
- **Pendiente de verificar con datos reales**: que el "último precio de venta" de la consulta de exportación sea el precio sin IVA correcto (se probó solo contra la base simulada) y definir el costo. Rendimiento de la consulta/Sincronizador sobre la base real sin medir (ver mediciones propuestas en la conversación: STATISTICS TIME/IO de la vista, índices de `CCA_M_Inventarios`, `is_read_committed_snapshot_on`); la idea es pasar a lectura incremental + conciliación completa periódica y `READ UNCOMMITTED`.

### 2b — Escritura app → WO: PENDIENTE (decisión del usuario: la venta debe descontar inventario en WO y se podrán crear clientes allá)
Cada remisión ya guarda `SincronizacionWo` (Pendiente/Sincronizada/Error), `DocumentoWorldOffice`, `ErrorSincronizacionWo`, `SincronizadaWoEn`; hoy todas quedan en `Pendiente`. Lo descubierto condiciona el diseño:
- El documento a crear es la **remisión de ventas `REM`** (efecto -1 en inventario; `REM` y `PED` comparten consecutivo; devolver = `DREM`; anular = `Cantidad=0`, `TotalRenglon=0`, `senAnulado=-1`).
- **Una `REM` CONTABILIZA**: además de cabecera + renglones escribe `TotalesDocumentos` y 4 asientos en `Asientos - Detalles` (cartera, 2 cuentas de ingreso, IVA). Insertar solo cabecera+renglones dejaría la contabilidad rota. Por eso se prefiere la **API/importación oficial de WO** (preguntas a soporte) y el SQL directo solo como respaldo probado en una **copia restaurada de la base** (nunca primero en producción), con autenticación de Windows por los triggers de auditoría, y tomando el consecutivo dentro de una transacción con bloqueo + reintento (`TrgI_ValidaLlave` revierte si hay llave duplicada).
- Crear clientes en WO: fila en `Terceros` (con "Cliente;" en el texto `Propiedades`), `Terceros - Direcciones` y, si se factura electrónicamente, `FE_Terceros_TipoResponsabilidades`.
- Pregunta de proceso abierta (contador/ventas): cómo registran hoy una venta de mostrador pagada en el momento (REM → FV → recibo de caja) — define si la app crea solo la REM o también factura/recibo.

## Datos de prueba
`Seed:DatosDemo=true` (puesto en el `docker-compose.yml` local) siembra 10 productos con códigos inventados `D-10xx` y 2 compradores demo si las tablas están vacías (`Api/DatosDemo.cs`). **No son los códigos reales de WO.** Quitar esa variable en producción.

## Comandos

```bash
dotnet build Jimaco.Remisiones.slnx
dotnet test Jimaco.Remisiones.TestUnitarios
dotnet ef migrations add <Nombre> --project Jimaco.Remisiones.Modelo --startup-project Jimaco.Remisiones.Api --output-dir Migraciones
cp .env.example .env && docker compose up -d --build     # db + api + web
```
Puertos locales (distintos a los otros proyectos para correr todos a la vez): web `4400`, api `8100`, db `127.0.0.1:1436`. Admin sembrado: `admin@jimaco.local` / `Admin123!` (**cambiar antes de uso real**).

Las pruebas con InMemory no cubren `CrearAsync` (la SEQUENCE no existe ahí); el ciclo crear → pagar → entregar se verificó contra SQL Server real vía Docker.

## Correo
`SmtpEmailSender` lee `Smtp:*` (Host, Port, UseSsl, Usuario, Password, RemitenteNombre, RemitenteEmail). Sin `Smtp:Host` el pago funciona igual y avisa que no pudo enviar. **Producción: usar un proveedor transaccional (Brevo/SendGrid), no una cuenta personal de Gmail** (Google inhabilita cuentas usadas para envío automatizado desde la nube). El dominio remitente debe estar en la lista blanca de SpamAssassin del cPanel de `jimaco.com.co` si los destinatarios son `@jimaco.com.co`.

## Despliegue (aún no desplegado)
Mismo Lightsail y patrón que Ferrealiados: stack propio en `/opt/...`, sin Caddy propio (se une a la red `jimacocotizaciones_default` y se agrega un bloque al Caddyfile de Jimaco), nombres de servicio **prefijados** (`jimaco-remisiones-*`, nunca `db`/`api`/`web`) y **nunca `--build` en el servidor**: construir imágenes local, `docker save | gzip`, `scp`, `docker load` y `docker compose ... up -d`. Backup de BD antes de migrar. El dominio `remisiones.jimaco.com.co` en `docker-compose.prod.yml` es un placeholder (falta crear el registro DNS).
