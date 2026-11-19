#!/usr/bin/env bash
# Ejecuta la revisión integral y guarda cada registro en una carpeta de evidencias.
# Uso: tests/revision/ejecutar_revision.sh <carpeta-evidencias> [parte]
#   parte: compilacion | memoria | navegador | sin-red | estatica | todo (por defecto)
# Variables opcionales (entorno de verificación en Linux, ver docs/compilacion.md):
#   DOTNET_BUILD_EXTRA  argumentos extra para "dotnet build" (feed local y envoltorio de ONNX Runtime)
#   UI_VERIFICACION     proyecto que compila la ventana con WinForms de Mono (solo Linux)
# En Windows basta con "dotnet build FerroSuite.slnx" y los mismos comandos de pruebas.
set -u
EVID="$(realpath -m "${1:?Indica la carpeta de evidencias}")"; PARTE="${2:-todo}"
RAIZ="$(cd "$(dirname "$0")/../.." && pwd)"; FRONT="$RAIZ/src/FerroSuite.Presentacion.Escritorio/frontend"
mkdir -p "$EVID"; cd "$RAIZ"
sello() { echo "# $(date -Iseconds) · $(uname -sr) · dotnet $(dotnet --version)"; }
clave() { grep "$1.prueba" "$EVID/servidor.log" | awk '{print $NF}'; }
iniciar() { pkill -f "FerroSuite.Pruebas" 2>/dev/null; sleep 1
  (dotnet run --no-build --project tests/FerroSuite.Pruebas -- servidor "$FRONT" "$@" > "$EVID/servidor.log" 2>&1 &)
  for _ in $(seq 1 40); do grep -q escuchando "$EVID/servidor.log" 2>/dev/null && return 0; sleep 1; done; echo "el servidor de prueba no inició"; return 1; }
nodo() { local salida="$1"; shift; ( sello; env CLAVE_ADMIN="$(clave admin)" CLAVE_BODEGA="$(clave bodega)" CLAVE_VENDEDOR="$(clave vendedor)" \
  CLAVE_CONTADOR="$(clave contador)" NODE_PATH="$(npm root -g)" "$@" ) > "$EVID/$salida" 2>&1; echo "$salida: $(tail -1 "$EVID/$salida")"; }

if [[ $PARTE == todo || $PARTE == compilacion ]]; then
  { sello
    for p in src/FerroSuite.Dominio src/FerroSuite.Aplicacion src/FerroSuite.Infraestructura src/FerroSuite.Presentacion.Comunicacion tests/FerroSuite.Pruebas; do
      echo "== dotnet build $p"; dotnet build "$p" -p:NuGetAudit=false ${DOTNET_BUILD_EXTRA:-} 2>&1 | grep -E "warning|error|Warning\(s\)|Error\(s\)|Build succeeded|Compilación correcta" | sort -u
    done
    if [[ -n "${UI_VERIFICACION:-}" ]]; then for c in Debug Release; do echo "== ventana (WinForms) $c"; dotnet build "$UI_VERIFICACION" -c $c -p:NuGetAudit=false ${DOTNET_BUILD_EXTRA:-} 2>&1 | grep -E "warning|error|Warning\(s\)|Error\(s\)" | sort -u; done; fi
    echo "== archivos que el ejecutable copia junto a sí (evaluación de MSBuild del proyecto de escritorio)"
    dotnet msbuild src/FerroSuite.Presentacion.Escritorio/FerroSuite.Presentacion.Escritorio.csproj -getItem:None -getItem:Content -p:EnableWindowsTargeting=true 2>/dev/null \
      | python3 -c "import json,sys,os; d=json.load(sys.stdin)['Items']; c=[(i.get('Link') or i['Identity']).replace(chr(92),'/') for k in ('None','Content') for i in d.get(k,[]) if i.get('CopyToOutputDirectory') in ('PreserveNewest','Always') and os.path.exists(i['FullPath'])]; print(len(c),'archivos;', sum(x.startswith('frontend') for x in c),'de frontend;', ', '.join(sorted(x for x in c if not x.startswith('frontend'))))"
    echo "== huella del modelo ONNX frente a la registrada en sus parámetros"
    python3 -c "import hashlib,json; h=hashlib.sha256(open('modelos/demanda_mlp.onnx','rb').read()).hexdigest(); e=json.load(open('modelos/demanda_mlp_normalizacion.json'))['verificacion_onnx']['sha256_onnx']; print('sha256', h, '==' if h==e else '!=', e)"
  } > "$EVID/01_compilacion.txt" 2>&1; echo "01_compilacion.txt listo"
fi
if [[ $PARTE == todo || $PARTE == memoria ]]; then
  ( sello; dotnet run --no-build --project tests/FerroSuite.Pruebas ) > "$EVID/02_pruebas_en_memoria.txt" 2>&1; echo "02: $(grep '^Total' "$EVID/02_pruebas_en_memoria.txt")"
fi
if [[ $PARTE == todo || $PARTE == navegador ]]; then
  iniciar && nodo 03_recorrido_general.txt node tests/pantallas/recorrido.js
  iniciar && nodo 04_pantalla_ia.txt env CAPTURAS="$EVID" node tests/pantallas/pantalla_ia.js
  iniciar --sin-modelo && nodo 05_pantalla_ia_sin_modelo.txt env MODO=sin-modelo CAPTURAS="$EVID" node tests/pantallas/pantalla_ia.js
  iniciar && nodo 06_revision_integral.txt node tests/pantallas/revision.js
  iniciar --modelo-falla && nodo 07_predictor_con_falla.txt node tests/pantallas/revision_predictor.js
  iniciar && nodo 07b_manipulacion_mensajes.txt node tests/revision/manipulacion_mensajes.js
  pkill -f "FerroSuite.Pruebas" 2>/dev/null; rm -f "$EVID/servidor.log"   # contiene claves sintéticas de la ejecución: no se conserva
fi
if [[ $PARTE == todo || $PARTE == sin-red ]]; then
  # Espacio de red aislado (sin interfaces hacia internet; solo la interfaz local para el servidor de prueba).
  unshare -rn bash -c "python3 '$RAIZ/tests/revision/interfaz_local.py' && '$0' '$EVID/sin_red' sin-red-dentro"
fi
if [[ $PARTE == sin-red-dentro ]]; then
  python3 -c "import socket
try: socket.create_connection(('1.1.1.1',443),timeout=3); print('RED: HAY ACCESO A INTERNET (la prueba no es válida)')
except OSError as e: print('RED: sin acceso a internet ->', e)" > "$EVID/00_red.txt"; cat "$EVID/00_red.txt"
  ( sello; dotnet run --no-build --project tests/FerroSuite.Pruebas ) > "$EVID/08_pruebas_en_memoria_sin_red.txt" 2>&1; echo "08: $(grep '^Total' "$EVID/08_pruebas_en_memoria_sin_red.txt")"
  iniciar && nodo 09_pantalla_ia_sin_red.txt env CAPTURAS="$EVID" node tests/pantallas/pantalla_ia.js
  iniciar && nodo 10_revision_integral_sin_red.txt node tests/pantallas/revision.js
  pkill -f "FerroSuite.Pruebas" 2>/dev/null; rm -f "$EVID/servidor.log"
fi
if [[ $PARTE == todo || $PARTE == estatica ]]; then
  ( sello; python3 tests/revision/verificaciones_estaticas.py ) > "$EVID/11_verificaciones_estaticas.txt" 2>&1; echo "11: $(tail -1 "$EVID/11_verificaciones_estaticas.txt")"
  ( sello; python3 tests/revision/equivalencia_modelo.py ) > "$EVID/12_equivalencia_modelo.txt" 2>&1; echo "12: $(tail -1 "$EVID/12_equivalencia_modelo.txt")"
  find ml tests -name __pycache__ -prune -exec rm -rf {} + 2>/dev/null
fi
