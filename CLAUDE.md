# CLAUDE.md

Guía para Claude Code (claude.ai/code) en este repositorio.

## Qué es

**Jimaco Remisiones**: app de **venta directa** de un conjunto cerrado de herramientas (las de `ProyectosJimaco/excel de herrramientas.xlsx`, una OC de pedido de feria). Se arma una **remisión**, se cobra con un **datáfono Redeban** (manual, sin integración) y se marca como pagada; cada remisión se envía por correo en PDF. También registra si lo vendido se entregó en sitio o queda para envío posterior.

Nació clonando [Ferrealiados Cotizaciones](../Ferrealiados.Cotizaciones) (mismo stack y patrones: .NET 10, capas Modelo/Negocio/Api, QuestPDF, JWT) con la marca de Jimaco (verde + logo). El frontend vive en el repo hermano **`Jimaco.Remisiones.Web`** (`../Jimaco.Remisiones.Web`); `docker-compose.yml` lo construye desde esa ruta relativa, así que ambos repos deben estar lado a lado.

## ⚠️ Alcance (decisión del usuario, 2026-10-15): SIN integración con World Office
La app es **autónoma**: el inventario, los clientes y la numeración de remisiones viven solo aquí. Antes se construyó una integración con WO (Sincronizador, foto de existencias, "reservado", importación del inventario por CSV desde SSMS, registrar en WO) y **se retiró por completo**; queda en el historial de git (commits `d2983ec`…`36c9f40` del backend) y la exploración de la base de WO en `ProyectosJimaco/RemisionesWO/hallazgos-worldoffice.md` (archivo histórico, ya no aplica). No reintroducir nada de WO sin que el usuario lo pida.

## Dominio

- **Producto**: `Codigo` (único, ej. `PJ-26550`), `Nombre`, `PrecioVenta` **por unidad ANTES de IVA** (decimal(18,2)), `Costo` (solo margen interno), `Iva` (0/5/19), `Stock` (int), `Activo`. El catálogo (crear/editar, precios, ajustes de stock) es **solo Admin**.
- **Inventario propio con kardex**: `Producto.Stock` solo cambia a través de `Kardex.Registrar` (`Negocio/Servicios/Kardex.cs`), que actualiza el stock y deja una línea en `MovimientoInventario` (`Tipo`: CargaInicial, Entrada, Venta, Reintegro, Ajuste; `Cantidad` con signo; `SaldoResultante`; `RemisionId`; `Usuario`; `Motivo`) en el mismo `SaveChanges`. **Nunca modificar `Stock` directamente.** El stock nunca queda negativo (lanza `InvalidOperationException`). `Producto.RowVersion` evita que dos ventas simultáneas sobrevendan (409).
  - **Crear** un producto con `StockInicial > 0` deja la línea `CargaInicial`.
  - **Remisión**: al armarla/editarla se valida `cantidad <= stock` (bloquea); **al marcarla Pagada sale el stock** (`Venta`, −cantidad) y si algún producto no alcanza no se guarda nada; **al anular una remisión Pagada se reintegra** (`Reintegro`, +cantidad). Pendientes y anuladas pendientes no tocan el stock (no hay "reserva": gana quien pague primero).
  - **Entradas y ajustes manuales** (solo Admin): `POST /api/productos/{id}/movimientos` — `Entrada` (cantidad > 0) o `Ajuste` (con signo, **motivo obligatorio**). Historial: `GET /api/productos/{id}/movimientos` (más reciente primero, con el consecutivo de la remisión).
- **Comprador**: se crea en el momento de la venta y se reutiliza buscando por documento (`GET /api/compradores/por-documento/{n}`). Campos: tipo de persona, tipo/número de documento (DV del NIT calculado por `DigitoVerificacion`), nombres separados o razón social, dirección, ciudad, **celular obligatorio** (10 dígitos que empiezan por 3; `Telefono` guarda solo esos dígitos; `NormalizarCelular`), **correo obligatorio**. Se guardan los datos que necesita contabilidad para crearlo luego en WO a mano (sin integración).
- **Remisión**: consecutivo propio `RMF-00001` desde una SQL SEQUENCE (`SecuenciaConsecutivoRemision`; se lee con ADO.NET directo porque EF envuelve `NEXT VALUE FOR` en una subconsulta y SQL Server lo rechaza, error 11719). Guarda **snapshot** del comprador y de cada ítem (código, nombre, precio, IVA, costo). **Descuento y retención** (2026-10-19): el descuento se digita en **% (0–50)** sobre el valor antes de IVA (`DescuentoPorcentaje`; `Descuento` guarda los pesos resultantes). La **retención** es opcional (casilla "Aplicar retención"), en pesos, y se resta del valor antes de IVA igual que el descuento (**opción B**, decisión del usuario: el IVA se calcula sobre lo que queda; debe ser menor a esa base). Ambos se reparten entre las tarifas de IVA (`RemisionCalculos`, duplicado en el front en `core/utils/calculos-remision.ts` solo para mostrar el total en vivo; el oficial lo calcula el backend). PDF y correo muestran "Descuento (x%)", "Retención" y "Valor antes de IVA".

### Ciclo de vida de una remisión
- `EstadoPago`: Pendiente → Pagada (o Anulada). Solo se **edita** mientras está Pendiente.
- Marcar **Pagada** (`POST /{id}/pagar`, hoy con referencia/voucher opcional): descuenta stock (ver arriba); si la entrega es `EnSitio` queda `Entregada`, si es `Envio` queda `Pendiente` hasta `POST /{id}/entregar`. Luego el correo sale **en segundo plano** (`CorreoRemisionCola`, `Channel` en memoria + `BackgroundService`: el pago responde en <1 s con `correoEnCola:true` y el front consulta la remisión hasta ver `correoEnviadoEn`); **un fallo del correo nunca revierte el pago** (se informa `correoEnviado`/`errorCorreo` y se reintenta con `POST /{id}/enviar-correo`).
- **Anular** exige motivo; una Pagada solo la anula un Admin (y devuelve el stock).
- Concurrencia optimista con `RowVersion` en remisión y producto (409).

### Roles
`Admin` (usuarios, catálogo, ajustes de stock, anular pagadas) y `Operador` (crear remisiones, cobrar, entregar). Se puede separar más adelante (cajero / admin / logística).

## Pendiente (acordado con el usuario, aún NO construido)
1. ~~Recibo Redeban~~ HECHO: `ReciboRedeban` obligatorio al pagar, en PDF y correo (falta avisar si se repite uno reciente).
2. ~~Correo al pagar~~ HECHO: sale al cliente y a `Correo:Contabilidad` (env `CORREO_CONTABILIDAD`), con la remisión completa en el cuerpo HTML + PDF adjunto. Pendiente: aviso al anular una pagada.
   - **WhatsApp (sin API, decisión del usuario)**: dos botones en el detalle de una remisión pagada. *WhatsApp* abre el chat del cliente (`wa.me`) con mensaje + enlace al PDF; *Compartir PDF* usa el menú de compartir del celular (`navigator.share` con archivo). El enlace es `GET /api/publico/remisiones/{id}/pdf?t=<token>` (anónimo; token = HMAC-SHA256 del id con `Jwt:Secret`, `EnlacePublicoController`); el token lo da `GET /api/remisiones/{id}/enlace-publico` (autenticado, solo Pagada). El enlace usa el origen desde el que se abre la app: en local no lo abre otro celular, en producción sí. El envío automático por Meta Cloud API/Twilio se descartó por ahora (requiere verificación de empresa en Meta, número dedicado y plantilla aprobada).
3. **Datos del cliente que pide WO**: ciudad de una lista (catálogo de ciudades), responsabilidad fiscal y tipo de contribuyente; botón para exportar clientes a Excel para contabilidad. Falta el formato que use contabilidad.
4. ~~Numeración~~ HECHO: prefijo `RMF-` desde 00001.
5. Precios de 4 productos que parecen derivados de un precio con IVA redondo (FYQ-500 = 2.450.000, FYQ-630 = 4.515.000, FYQ-1000 = 5.670.000, HS-520A = 343.000 con IVA) pero que el usuario pidió cargar **tal cual** del archivo de distribuidores (`PrecioVenta` entero): con 19 % dan 2.449.999, 4.514.998, 5.670.000 y 342.999,65. Y las referencias `YQK-70/120/300` (distribuidores) vs `TQK-…` (OC): se cargó el nombre de la OC.
6. Despliegue (no desplegado), SMTP real, dominio.

## Carga de las herramientas
`scripts/herramientas.json` (15 productos) sale de cruzar **`excel de herrramientas.xlsx`** (OC 17606 de WO: código, nombre y cantidad **tal como están allí**) con **`HERRAMIENTA DISTRIBUIDORES.xlsx`** (precio por unidad antes de IVA, **tal cual**) por referencia; todas las cantidades coinciden. `node scripts/cargar-herramientas.mjs <urlApi> <email> <password>` los crea con su stock inicial (idempotente: si el código existe, 409 y no toca el stock). El cruce completo para revisar está en `ProyectosJimaco/Cruce herramientas - productos.xlsx`.

## Datos de demostración
`Seed:DatosDemo=true` (env `SEED_DATOS_DEMO=true` en `.env`, **apagado por defecto**) siembra 3 productos `DEMO-00x` con 50 unidades y 2 compradores si las tablas están vacías (`Api/DatosDemo.cs`). No usar en producción.

## Comandos

```bash
dotnet build Jimaco.Remisiones.slnx
dotnet test Jimaco.Remisiones.TestUnitarios
dotnet ef migrations add <Nombre> --project Jimaco.Remisiones.Modelo --startup-project Jimaco.Remisiones.Api --output-dir Migraciones
cp .env.example .env && docker compose up -d --build     # db + api + web
node scripts/cargar-herramientas.mjs http://localhost:8100/api admin@jimaco.local 'Admin123!'
```
Puertos locales (distintos a los otros proyectos para correr todos a la vez): web `4400`, api `8100`, db `127.0.0.1:1436`. Admin sembrado: `admin@jimaco.local` / `Admin123!` (**cambiar antes de uso real**). La migración es una sola (`Inicial`): la app nunca se desplegó, así que al cambiar el modelo se puede regenerar desde cero (borrar `Migraciones/`, `docker compose down -v`).

Las pruebas con InMemory no cubren `CrearAsync` de remisión (la SEQUENCE no existe ahí); el ciclo crear → pagar → anular y el stock se verificó contra SQL Server real vía Docker y la API. **Gotcha**: `curl` en Git Bash de Windows manda mal las tildes en JSON (400); probar con texto sin tildes o desde el navegador.

## Correo
**Medición (2026-10-19)**: el SMTP del hosting acepta un mensaje a un correo interno (`@jimaco.com.co`) en ~0,5 s pero a un correo externo (hotmail) tarda **~80 s** en la fase DATA (conectar+login solo ~2 s). Por eso el envío es en segundo plano y los dos correos (cliente y contabilidad) salen en paralelo; cambiar de cuenta no ayuda (Aprobaciones usa el mismo servidor). Si molesta la demora al cliente, el arreglo real es un proveedor transaccional (Brevo/SendGrid). Tras un envío largo se recarga la remisión (`ReloadAsync`) antes de marcar `CorreoEnviadoEn` para no chocar con el `RowVersion`.
`SmtpEmailSender` lee `Smtp:*` (Host, Port, UseSsl, Usuario, Password, RemitenteNombre, RemitenteEmail). Sin `Smtp:Host` el pago funciona igual y avisa que no pudo enviar. **Producción: usar un proveedor transaccional (Brevo/SendGrid), no una cuenta personal de Gmail** (Google inhabilita cuentas usadas para envío automatizado desde la nube). Ojo: un remitente dentro del mismo hosting de `jimaco.com.co` que escribe a otro `@jimaco.com.co` puede quedar entregado localmente en el servidor y no en el buzón real (M365); y SpamAssassin del cPanel puede marcar `***SPAM***` salvo lista blanca del remitente.

## Despliegue (PRUEBAS en Lightsail desde 2026-10-16)
URL: `https://remisiones.jimaco.com.co` (registro A `remisiones` en la zona DNS de cPanel → 54.232.227.230; también sigue `https://remisiones.54-232-227-230.sslip.io`) (HTTPS por el Caddy compartido `jimaco-caddy`; bloque agregado a `/opt/jimaco/Jimaco.Cotizaciones/Caddyfile`, con backup `.bak-<fecha>` al lado). Carpeta del servidor: `/opt/jimaco-remisiones/` (`docker-compose.yml` con imágenes ya construidas + `.env` con secretos generados en el servidor; el compose es distinto al del repo porque no hay SQL propio).
- **El servidor es chico (3,7 GB RAM)**: no hay sitio para otro SQL Server. La app usa la base `JimacoRemisiones` (usuario `remisiones_app`, solo `db_owner` de esa base) **dentro del SQL Server de Ferrealiados** (`ferrealiados-db`). Se añadió un swap de 2 GB (`/swapfile`). Antes de la feria: subir el plan a 8 GB y darle SQL propio.
- **Nunca construir en el servidor.** Redeploy: construir en local (`docker build -t jimaco-remisiones-api:latest -f Jimaco.Remisiones.Api/Dockerfile .` en este repo; `docker build -t jimaco-remisiones-web:latest .` en el repo Web), `docker save ... | gzip`, `scp`, `gunzip -c | docker load` y `docker compose up -d` en `/opt/jimaco-remisiones`. Backup de la base antes de migrar (BACKUP DATABASE desde `ferrealiados-db`).
- Nombres de servicio únicos (`jimaco-remisiones-*`) por el alias DNS de la red compartida `jimacocotizaciones_default`. Jimaco Cotizaciones está detenido (su compose contiene el Caddy y la red: **no hacer `down` de ese stack**).
- La contraseña del admin se cambió tras el primer despliegue (la sembrada `Admin123!` ya no sirve en el servidor). Correo: mismas credenciales SMTP de Aprobaciones (`sistemas@jimaco.com.co`); `Correo__Contabilidad` apunta a `sistemas@jimaco.com.co` mientras se prueba.
