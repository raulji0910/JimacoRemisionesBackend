// Crea usuarios con rol Operador (pueden armar remisiones, cobrar y entregar; no anulan remisiones pagadas ni administran el catálogo).
// Idempotente: si el correo ya existe no lo toca.
//
//   node scripts/crear-operarios.mjs <urlApi> <adminEmail> <adminPassword> <passwordOperarios> <archivo.json>
//
// archivo.json: [{ "nombre": "Ana Pérez", "email": "ana@jimaco.com.co" }, ...]
import { readFileSync } from 'node:fs';

const [urlApi, adminEmail, adminPassword, passwordOperarios, archivo] = process.argv.slice(2);
if (!archivo) {
  console.error('Uso: node scripts/crear-operarios.mjs <urlApi> <adminEmail> <adminPassword> <passwordOperarios> <archivo.json>');
  process.exit(1);
}

const lista = JSON.parse(readFileSync(archivo, 'utf-8'));

const login = await fetch(`${urlApi}/auth/login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: adminEmail, password: adminPassword })
});
if (!login.ok) {
  console.error('No se pudo iniciar sesión como administrador:', login.status);
  process.exit(1);
}
const { token } = await login.json();
const cabeceras = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };

const existentes = await (await fetch(`${urlApi}/usuarios`, { headers: cabeceras })).json();
const correosExistentes = new Set(existentes.map((u) => u.email.toLowerCase()));

let creados = 0;
let omitidos = 0;
let errores = 0;
for (const { nombre, email } of lista) {
  if (correosExistentes.has(email.toLowerCase())) {
    console.log(`  ya existe  ${email}`);
    omitidos++;
    continue;
  }
  const respuesta = await fetch(`${urlApi}/usuarios`, {
    method: 'POST',
    headers: cabeceras,
    body: JSON.stringify({ nombre, email, password: passwordOperarios, rol: 'Operador' })
  });
  if (respuesta.ok) {
    console.log(`  creado     ${email}  (${nombre})`);
    creados++;
  } else {
    console.log(`  ERROR ${respuesta.status} ${email}: ${await respuesta.text()}`);
    errores++;
  }
}
console.log(`\nListo: ${creados} creados, ${omitidos} ya existían, ${errores} con error.`);
