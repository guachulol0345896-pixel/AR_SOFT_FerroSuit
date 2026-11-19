"""Revisión estática de FerroSuite: referencias y rutas, acciones, duplicados, claves expuestas y dependencias.
Uso: python3 tests/revision/verificaciones_estaticas.py  (desde la raíz del repositorio). Solo lee archivos."""
import json, os, re, sys
from collections import Counter
from pathlib import Path

RAIZ = Path(__file__).resolve().parents[2]
FRONT = RAIZ / "src/FerroSuite.Presentacion.Escritorio/frontend"
EXCLUIR = {"bin", "obj", "__pycache__", "node_modules", ".git"}
res = []

def ok(i, nombre, cond, detalle=""):
    res.append((i, nombre, bool(cond), detalle))

def archivos(base, *ext):
    for r, ds, fs in os.walk(base):
        ds[:] = [d for d in ds if d not in EXCLUIR]
        for f in fs:
            if not ext or f.endswith(ext):
                yield Path(r) / f

# ---------- S1: scripts, estilos y enlaces de las pantallas ----------
rotos, enlaces_rotos, dup_scripts = [], [], []
for html in FRONT.glob("*.html"):
    t = html.read_text(encoding="utf-8")
    refs = re.findall(r'(?:src|href)="([^"#?]+)', t)
    scripts = re.findall(r'<script src="([^"]+)"', t)
    dup_scripts += [f"{html.name}: {s}" for s, n in Counter(scripts).items() if n > 1]
    for r in refs:
        if r.startswith(("http:", "https:", "mailto:", "data:")):
            continue
        if not (FRONT / r).exists():
            rotos.append(f"{html.name} → {r}")
for js in archivos(FRONT, ".js"):
    for r in re.findall(r'href="([a-z_]+\.html)', js.read_text(encoding="utf-8")) + re.findall(r'location\.href = "([a-z_]+\.html)', js.read_text(encoding="utf-8")):
        if not (FRONT / r).exists():
            enlaces_rotos.append(f"{js.name} → {r}")
ok("S1", "Todos los scripts, estilos y páginas que referencian las pantallas existen", not rotos and not enlaces_rotos, "; ".join(rotos + enlaces_rotos))
ok("S2", "Ninguna pantalla carga el mismo script dos veces", not dup_scripts, "; ".join(dup_scripts))

# ---------- S3: recursos externos (funcionamiento sin internet) ----------
externos = []
for f in list(archivos(FRONT, ".html", ".js", ".css")):
    for u in re.findall(r'(?:src=|href=|url\(|@import\s+)["\']?(https?://[^"\')\s]+)', f.read_text(encoding="utf-8")):
        externos.append(f"{f.relative_to(FRONT)}: {u}")
ok("S3", "Las pantallas no cargan scripts, estilos, fuentes ni imágenes de internet", not [e for e in externos if "github.com" not in e],
   "solo enlaces a GitHub (abren el navegador, no se cargan): " + str(len(externos)) if externos else "")

# ---------- S4-S6: acciones del Puente ----------
puente = (RAIZ / "src/FerroSuite.Presentacion.Comunicacion/Puente.cs").read_text(encoding="utf-8")
permisos = re.findall(r'\["([a-zA-Z.]+)"\]\s*=\s*\[', puente)
casos = re.findall(r'case "([a-zA-Z.]+)":', puente)
dup_perm = [a for a, n in Counter(permisos).items() if n > 1]
dup_case = [a for a, n in Counter(casos).items() if n > 1]
ok("S4", "Sin acciones duplicadas en la tabla de permisos ni en el despacho del Puente", not dup_perm and not dup_case, f"permisos {dup_perm} casos {dup_case}")
publicas = {"sesion.iniciar", "sesion.cerrar", "sesion.actual", "sesion.cambiarClave", "sistema.estado"}
sin_permiso = sorted(set(casos) - set(permisos) - publicas)
ok("S5", "Toda acción despachada tiene permiso declarado (salvo las de sesión y estado, públicas por diseño)", not sin_permiso, ", ".join(sin_permiso))
usadas = set()
for js in archivos(FRONT, ".js"):
    usadas |= set(re.findall(r'pedir\("([a-zA-Z.]+)"', js.read_text(encoding="utf-8")))
inexistentes = sorted(usadas - set(casos))
sin_pantalla = sorted(set(casos) - usadas)
ok("S6", "Toda acción que piden las pantallas existe en el backend", not inexistentes, ", ".join(inexistentes))
ok("S7", "Toda acción del backend tiene una pantalla que la usa", not sin_pantalla, "sin pantalla: " + ", ".join(sin_pantalla))

# ---------- S8: archivos de frontend que ninguna pantalla carga ----------
cargados = set()
for html in FRONT.glob("*.html"):
    cargados |= set(re.findall(r'(?:src|href)="([^"#?]+\.(?:js|css))"', html.read_text(encoding="utf-8")))
huérfanos = sorted(str(p.relative_to(FRONT)) for p in archivos(FRONT, ".js", ".css") if str(p.relative_to(FRONT)) not in cargados)
ok("S8", "No hay scripts ni estilos sin usar en el frontend", not huérfanos, ", ".join(huérfanos))

# ---------- S9: identificadores de prueba duplicados ----------
def ids_de(*rutas):
    salida = []
    for f in rutas:
        salida += re.findall(r'Check\("([A-Z]+[0-9]+[a-z]?)"', f.read_text(encoding="utf-8"))
    return salida
pr = RAIZ / "tests/FerroSuite.Pruebas"
memoria = ids_de(pr / "Program.cs", *[f for f in (pr / "Herramientas").glob("*.cs")])
bd = ids_de(pr / "PruebasBaseDatos.cs")
dup_ids = sorted(i for i, n in Counter(memoria).items() if n > 1)
dup_bd = sorted(i for i, n in Counter(bd).items() if n > 1)
cruzados = sorted(set(memoria) & set(bd))
js_ids = []
for f in (RAIZ / "tests/pantallas").glob("*.js"):
    c = Counter(re.findall(r"ok\('([A-Z]+[0-9]+)'", f.read_text(encoding="utf-8")))
    js_ids += [f"{f.name}:{i}" for i, n in c.items() if n > 1]
ok("S9", "Los identificadores de las pruebas son únicos dentro de cada suite (trazabilidad)", not dup_ids and not dup_bd and not js_ids,
   f"repetidos en la suite en memoria: {dup_ids}; en la suite SQL: {dup_bd}; en navegador: {js_ids}; (además {len(cruzados)} identificadores se repiten entre la suite en memoria y la SQL)")

# ---------- S10: claves y secretos ----------
patrones = {
    "clave de Google (AIza…)": r"AIza[0-9A-Za-z_\-]{30,}",
    "clave privada": r"-----BEGIN [A-Z ]*PRIVATE KEY-----",
    "token de GitHub": r"gh[pousr]_[A-Za-z0-9]{30,}",
    "clave de OpenAI/Anthropic": r"sk-(?:ant-)?[A-Za-z0-9_\-]{20,}",
    "contraseña en cadena de conexión": r"(?i)(?:password|pwd)\s*=\s*[^;\"'\s{]{3,}",
    "ApiKey con valor": r'(?i)"apikey"\s*:\s*"[^"]{8,}"',
    "clave literal en C#": r'(?i)(?:clave|password|contrase(?:ñ|n)a)\s*=\s*"[^"]{6,}"',
}
hallazgos = []
for f in archivos(RAIZ):
    if f.suffix.lower() in {".png", ".onnx", ".npz", ".pt", ".zip", ".dll", ".so"}:
        continue
    try:
        t = f.read_text(encoding="utf-8")
    except Exception:
        continue
    for nombre, p in patrones.items():
        for m in re.finditer(p, t):
            hallazgos.append(f"{f.relative_to(RAIZ)}:{t[:m.start()].count(chr(10)) + 1} ({nombre}): {m.group(0)[:60]}")
producto = [h for h in hallazgos if not h.startswith("tests/")]
pruebas = [h for h in hallazgos if h.startswith("tests/")]
ok("S10", "Sin claves, tokens ni contraseñas en el código del programa, la configuración, la base de datos, ml/ ni la documentación", not producto, " | ".join(producto[:12]))
# En las pruebas, los literales encontrados deben ser valores deliberadamente inválidos o centinelas (revisados a mano).
centinelas = ("Clave-equivocada", "Corta123", "xnuevo.usuario9", "Clave-mala-", "Mala-clave-123", "NO-DEBE-LEERSE", "CLAVE-SINTETICA-DE-PRUEBA")
no_revisados = [h for h in pruebas if not any(c in h for c in centinelas)]
ok("S10b", "En las pruebas solo hay claves deliberadamente inválidas o centinelas (las válidas se generan al azar en cada ejecución)", not no_revisados,
   f"{len(pruebas)} literales revisados" + (": " + " | ".join(no_revisados[:8]) if no_revisados else ""))
# appsettings.json admite comentarios // (JSON con comentarios de .NET): se quitan antes de leerlo.
cfg = json.loads(re.sub(r"^\s*//.*$", "", (RAIZ / "config/appsettings.json").read_text(encoding="utf-8"), flags=re.M))
ok("S11", "config/appsettings.json no tiene campos de clave ni credenciales", not re.search(r"(?i)key|clave|password|secret|token", json.dumps(cfg)), json.dumps(cfg)[:200])
gi = (RAIZ / ".gitignore").read_text(encoding="utf-8") if (RAIZ / ".gitignore").exists() else ""
ok("S12", ".gitignore excluye compilados y archivos locales de configuración/secretos", all(x in gi for x in ("bin", "obj")) and re.search(r"(?i)local|secret|\.env|user", gi), gi.replace("\n", " ")[:300])

# ---------- S13-S15: dependencias ----------
codigo = {p: p.read_text(encoding="utf-8") for p in archivos(RAIZ / "src", ".cs")}
def usa(prefijo, carpeta):
    return any(prefijo in t for p, t in codigo.items() if carpeta in str(p))
paquetes = {}
for cs in archivos(RAIZ / "src", ".csproj"):
    for n, v in re.findall(r'PackageReference Include="([^"]+)" Version="([^"]+)"', cs.read_text(encoding="utf-8")):
        paquetes[n] = (v, cs.parent.name)
uso = {
    "Microsoft.Data.SqlClient": usa("Microsoft.Data.SqlClient", "Infraestructura"),
    "Microsoft.Extensions.Identity.Core": usa("Microsoft.AspNetCore.Identity", "Infraestructura"),
    "Microsoft.ML.OnnxRuntime": usa("Microsoft.ML.OnnxRuntime", "Infraestructura"),
    "Microsoft.Web.WebView2": usa("Microsoft.Web.WebView2", "Escritorio"),
}
sin_uso = [n for n in paquetes if not uso.get(n, False)]
ok("S13", "Cada paquete NuGet se usa en el código del proyecto que lo referencia", not sin_uso, f"{len(paquetes)} paquetes: " + ", ".join(f"{n} {v[0]} ({v[1]})" for n, v in paquetes.items()) + (f"; sin uso: {sin_uso}" if sin_uso else ""))
dominio = [p for p in archivos(RAIZ / "src/FerroSuite.Dominio", ".csproj")]
ok("S14", "El dominio no depende de paquetes externos ni de otras capas", not re.search(r"PackageReference|ProjectReference", dominio[0].read_text(encoding="utf-8")))
aplicacion = (RAIZ / "src/FerroSuite.Aplicacion/FerroSuite.Aplicacion.csproj").read_text(encoding="utf-8")
ok("S15", "La aplicación solo depende del dominio (sin SQL, ONNX ni WebView2)", "PackageReference" not in aplicacion and not any(x in t for p, t in codigo.items() if "Aplicacion" in str(p) for x in ("Microsoft.Data.SqlClient", "Microsoft.ML.OnnxRuntime", "WebView2")))
py = {}
for f in archivos(RAIZ / "ml", ".py"):
    for m in re.findall(r"^\s*(?:import|from)\s+([a-zA-Z_]+)", f.read_text(encoding="utf-8"), re.M):
        py[m] = py.get(m, 0) + 1
req = [l.split("==")[0].strip() for l in (RAIZ / "ml/requirements.txt").read_text(encoding="utf-8").splitlines() if "==" in l]
nombre_import = {"torch": "torch", "numpy": "numpy", "pandas": "pandas", "matplotlib": "matplotlib", "onnx": "onnx", "onnxscript": "onnxscript", "onnxruntime": "onnxruntime"}
# onnxscript no se importa: lo exige torch.onnx.export(dynamo=True), que usa ml/exportar_onnx.py.
indirectos = {"onnxscript": "dynamo=True" in (RAIZ / "ml/exportar_onnx.py").read_text(encoding="utf-8")}
py_sin_uso = [r for r in req if nombre_import.get(r, r) not in py and not indirectos.get(r)]
ok("S16", "Cada paquete de ml/requirements.txt se importa en el código de Python", not py_sin_uso, f"requisitos: {req}; onnxscript es dependencia indirecta del exportador dynamo" + (f"; sin uso: {py_sin_uso}" if py_sin_uso else ""))

# ---------- S17: rutas que usa el programa en tiempo de ejecución ----------
esperadas = ["frontend", "modelos", "BaseDatos", "appsettings.json"]
fuentes = {"frontend": FRONT, "modelos": RAIZ / "modelos/demanda_mlp.onnx", "BaseDatos": RAIZ / "database/migraciones", "appsettings.json": RAIZ / "config/appsettings.json"}
ok("S17", "Las carpetas y archivos que el programa busca junto al ejecutable existen en el repositorio", all(p.exists() for p in fuentes.values()), ", ".join(f"{k}: {'sí' if p.exists() else 'NO'}" for k, p in fuentes.items()))

# ---------- S18: enlaces internos de la documentación ----------
md_rotos = []
for f in list(archivos(RAIZ, ".md")):
    for r in re.findall(r"\]\(([^)#\s]+)(?:#[^)]*)?\)", f.read_text(encoding="utf-8")):
        if r.startswith(("http", "mailto:", "computer:")):
            continue
        if not (f.parent / r).resolve().exists():
            md_rotos.append(f"{f.relative_to(RAIZ)} → {r}")
ok("S18", "Los enlaces internos de la documentación apuntan a archivos que existen", not md_rotos, "; ".join(md_rotos[:10]))

for i, n, c, d in res:
    print(f"{'PASA ' if c else 'FALLA'} | {i} | {n}{' — ' + d if d else ''}")
print(f"\n{sum(1 for r in res if r[2])}/{len(res)} verificaciones correctas")
