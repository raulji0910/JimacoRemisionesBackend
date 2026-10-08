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

## Fase 2 pendiente: inventario en World Office (NO construida)

Decisión del usuario (2026-10-08): la venta debe **descontar inventario en World Office**, y lo hará el **Sincronizador** (como ya hace `Jimaco.Aprobaciones.Sincronizador` con la aprobación de OC). Hoy cada remisión guarda `SincronizacionWo` (Pendiente/Sincronizada/Error), `DocumentoWorldOffice`, `ErrorSincronizacionWo` y `SincronizadaWoEn`, y todas quedan en `Pendiente`. Pendiente definir/construir:
- Qué documento de WO representa la venta (candidato: remisión de ventas, prefijo `REM` en `[CuentasContables - Asientos]` + renglones en `CCA_M_Inventarios`) y cómo se escribe sin romper kardex/costos/contabilidad — **nunca un UPDATE directo al stock**; debe entrar como documento de WO. Ver el esquema ya mapeado en `ProyectosJimaco/WorkflowDocumentos/esquema-worldoffice-oc.md` y las lecciones de escritura a WO (triggers de auditoría, requiere autenticación de Windows) en el CLAUDE.md de `Jimaco.Aprobaciones`.
- Endpoints para que el Sincronizador tome remisiones Pagadas con `SincronizacionWo = Pendiente` y devuelva el resultado.
- Carga/sincronización de productos (código WO, precio, costo, IVA, stock) y de compradores (solo terceros con propiedad Cliente) desde WO, y empuje de los creados localmente (`Origen = Local`).

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
