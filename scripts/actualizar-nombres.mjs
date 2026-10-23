// Actualiza SOLO el nombre de los productos de scripts/herramientas.json que ya existen (por código). No toca precio, IVA, estado ni stock.
// Idempotente: si el nombre ya coincide no hace nada.
//
//   node scripts/actualizar-nombres.mjs <urlApi> <adminEmail> <adminPassword>
import { readFileSync } from 'node:fs';

const [urlApi, email, password] = process.argv.slice(2);
if (!password) {
  console.error('Uso: node scripts/actualizar-nombres.mjs <urlApi> <adminEmail> <adminPassword>');
  process.exit(1);
}

const herramientas = JSON.parse(readFileSync(new URL('./herramientas.json', import.meta.url), 'utf-8'));

const login = await fetch(`${urlApi}/auth/login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email, password })
});
if (!login.ok) {
  console.error('No se pudo iniciar sesión:', login.status);
  process.exit(1);
}
const { token } = await login.json();
const cabeceras = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };

const pagina = await (await fetch(`${urlApi}/productos?tamanoPagina=100`, { headers: cabeceras })).json();
const porCodigo = new Map(pagina.items.map((p) => [p.codigo, p]));

let cambiados = 0;
let iguales = 0;
let faltan = 0;
for (const h of herramientas) {
  const actual = porCodigo.get(h.codigo);
  if (!actual) {
    console.log(`  no existe  ${h.codigo}`);
    faltan++;
    continue;
  }
  if (actual.nombre === h.nombre) {
    iguales++;
    continue;
  }
  const respuesta = await fetch(`${urlApi}/productos/${actual.id}`, {
    method: 'PUT',
    headers: cabeceras,
    body: JSON.stringify({
      codigo: actual.codigo,
      nombre: h.nombre,
      unidadMedida: actual.unidadMedida,
      precioVenta: actual.precioVenta,
      costo: actual.costo,
      iva: actual.iva,
      activo: actual.activo
    })
  });
  if (respuesta.ok) {
    console.log(`  ${h.codigo}  ${actual.nombre}\n         -> ${h.nombre}`);
    cambiados++;
  } else {
    console.log(`  ERROR ${respuesta.status} ${h.codigo}: ${await respuesta.text()}`);
  }
}
console.log(`\nListo: ${cambiados} renombrados, ${iguales} ya estaban igual, ${faltan} no existen.`);
