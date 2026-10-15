// Carga las herramientas (scripts/herramientas.json) en la API de Remisiones, con su stock inicial.
//
// Los datos salen de cruzar "excel de herrramientas.xlsx" (OC 17606 de WO: código, nombre y cantidad,
// tal cual están allí) con "HERRAMIENTA DISTRIBUIDORES.xlsx" (precio por unidad ANTES de IVA, tal cual).
//
// Uso (Node 18+):
//   node scripts/cargar-herramientas.mjs <urlApi> <email> <password>
//   ej: node scripts/cargar-herramientas.mjs http://localhost:8100/api admin@jimaco.local Admin123!
//
// Es idempotente a medias: si un código ya existe la API responde 409 y se informa, sin duplicar ni
// tocar el stock de lo que ya está cargado.
import { readFileSync } from 'node:fs';

const [, , url, email, password] = process.argv;
if (!url || !email || !password) {
  console.error('Uso: node scripts/cargar-herramientas.mjs <urlApi> <email> <password>');
  process.exit(1);
}

const productos = JSON.parse(readFileSync(new URL('./herramientas.json', import.meta.url), 'utf8'));

const login = await fetch(`${url}/auth/login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email, password })
});
if (!login.ok) {
  console.error(`No se pudo iniciar sesión (${login.status}).`);
  process.exit(1);
}
const { token } = await login.json();

let creados = 0;
let existentes = 0;
let fallos = 0;

for (const p of productos) {
  const r = await fetch(`${url}/productos`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({ ...p, costo: null })
  });

  if (r.status === 201) {
    creados++;
    console.log(`  creado   ${p.codigo}  stock ${p.stockInicial}  precio ${p.precioVenta}  ${p.nombre}`);
  } else if (r.status === 409) {
    existentes++;
    console.log(`  existe   ${p.codigo}  (${(await r.json()).mensaje})`);
  } else {
    fallos++;
    console.error(`  ERROR ${r.status} en ${p.codigo}: ${await r.text()}`);
  }
}

console.log(`\nListo: ${creados} creados, ${existentes} ya existían, ${fallos} con error.`);
process.exit(fallos > 0 ? 1 : 0);
