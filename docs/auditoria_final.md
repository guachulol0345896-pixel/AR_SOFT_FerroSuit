# Auditoría final antes de la publicación

> **Actualización (cierre, 18-nov-2026):** después de esta auditoría se corrigieron D-01, D-03, D-04 y D-06 y las evidencias se movieron a `evidencias/`. Estado vigente en [INFORME_FINAL_FERROSUITE.md](../INFORME_FINAL_FERROSUITE.md).


**Fecha:** 18 de noviembre de 2026 · **Versión:** 0.5.0 · **Alcance:** todo el repositorio nuevo de FerroSuite

## Veredicto

El repositorio está **listo para que el equipo lo revise y lo publique desde sus propias cuentas**:
- No hay historial Git.
- No hay compilados, archivos temporales ni secretos.
- La documentación coincide con el código.
- Todo lo que se pudo ejecutar pasa, salvo **8 defectos conocidos**, todos documentados.

**No está verificado de punta a punta**, porque la auditoría se hizo en Linux, sin Windows ni SQL Server. Faltan:
- la ventana de escritorio en ejecución;
- la compilación con los paquetes NuGet reales;
- las 51 pruebas contra SQL Server.

Antes de publicar como versión final, el equipo debe completar las [evidencias pendientes](#6-evidencias-necesarias-para-cerrar-la-verificación).

## 1. Resultado de los 13 puntos

Estados:
- **CUMPLE**: ejecutado o revisado aquí, sin hallazgos que lo impidan.
- **CUMPLE CON OBSERVACIONES**: cumple, con hallazgos documentados.
- **PENDIENTE**: no se puede ejecutar en este entorno.

| # | Punto | Estado | Evidencia y detalle |
|---|---|---|---|
| 1 | La solución compila | **CUMPLE CON OBSERVACIONES** | 5 proyectos y el código de la ventana (Debug y Release): 0 errores, 0 advertencias (`evidencias/revision/01_compilacion.txt`). La solución completa con los paquetes reales **no se pudo compilar aquí** (`NETSDK1100` y sin acceso a nuget.org; `01b_compilacion_solucion_completa.txt`): **PENDIENTE en Windows** |
| 2 | El programa de escritorio se ejecuta | **PENDIENTE** | Requiere Windows con WebView2. El arranque está revisado en el código (WebView2, preparación de la base, administrador inicial). Los 48 archivos que se copian junto al ejecutable existen (`01_compilacion.txt`) |
| 3 | Los módulos operativos funcionan | **CUMPLE CON OBSERVACIONES** | Backend real con repositorio en memoria: 143/143. Pantallas reales en Chromium: 28/28, 17/17, 3/3, 4/4 y 26/29 en la revisión integral. Las 3 fallas son D-01, D-03 y D-04 (sección 4) |
| 4 | SQL Server guarda los datos | **PENDIENTE** | 51 pruebas listas (`-- --bd`: migraciones, restricciones, transacciones, concurrencia, reapertura, migración desde la v1), sin ejecutar: no hay SQL Server aquí. En Linux, `-- --bd` falla al conectarse, como se esperaba. La misma lógica pasa con el repositorio en memoria |
| 5 | Autenticación y permisos aplicados | **CUMPLE** | Matriz de 37 acciones × 4 roles y sin sesión, con mensajes directos al `Puente` (MZ). Servicios llamados sin el `Puente` (D1). Ingreso, bloqueo, vencimiento, cambio obligatorio de clave (A, U). Auditoría sin claves (AU4). Pantallas por rol (E3-E13, IA16) |
| 6 | Red neuronal entrenada y disponible en ONNX | **CUMPLE** | `modelos/demanda_mlp.onnx` carga con huella, parámetros y grafo verificados (PI1). Su SHA-256 `08eb91cf…5cce` coincide con la registrada (`01_compilacion.txt`). Al reentrenar con `python ml/ejecutar_todo.py` en una copia del repositorio se obtuvieron las mismas huellas de datos, pesos, predicciones y métricas, y un `.onnx` **idéntico byte a byte** al entrenar en la misma ruta. En otra ruta los pesos y las salidas son idénticos y solo cambia la ruta guardada en los metadatos (observación O-2) |
| 7 | El programa usa realmente sus predicciones | **CUMPLE** | La ventana crea `PronosticadorOnnx` y lo entrega a `PronosticoServicio` (`VentanaPrincipal.cs`, líneas 57 y 70). C# da la misma salida que Python: 3,61 frente a 3,6096 (PI3, PD1). La pantalla muestra el valor del backend: SIN-001 con la red, 6,37/día (IA3); sin el modelo, la misma pantalla usa la regla, 5,9/día, y lo indica (IN1, captura `ia_modelo_no_disponible.png`). Si el predictor falla, se ve el respaldo (RP1-RP4) |
| 8 | Pruebas y evidencias de integración | **CUMPLE** | Registros fechados en `evidencias/revision/` y capturas en `evidencias/`. Informe por área en [informe_pruebas.md](informe_pruebas.md). Sin internet: 143/143, 17/17 y 26/29 (`sin_red/`) |
| 9 | El README coincide con la aplicación | **CUMPLE** | Comprobación automática: paquetes y versiones = `.csproj`; valores por defecto = `appsettings.json`; pantallas = archivos `.html`; códigos de rol = `Rol.cs`; textos de menú = `VentanaPrincipal.cs`; defectos = informe; cifras de pruebas = registros; la medición del ejemplo, idéntica carácter por carácter. Los 68 enlaces y anclas existen |
| 10 | La documentación coincide con los componentes | **CUMPLE** (tras las correcciones de la sección 6) | Las rutas citadas en `docs/`, `ml/`, `modelos/`, `database/` y `tests/` existen. Las únicas que no son las del mapa de migración del repositorio anterior (histórico) y el archivo de resultados de Windows que falta generar. Los documentos de cada etapa conservan las cifras de su momento, marcadas como tales |
| 11 | Sin claves, contraseñas, tokens ni archivos privados | **CUMPLE CON OBSERVACIONES** | Búsqueda de claves de Google, OpenAI y GitHub, claves privadas, contraseñas en cadenas de conexión y literales: 0 en el programa, la configuración, SQL, `ml/` y la documentación (S10). En las pruebas solo hay claves deliberadamente inválidas (S10b). Los registros del servidor de prueba, que contienen claves aleatorias, se borran. Los metadatos de los documentos históricos solo tienen nombres del equipo. Observaciones: D-05 (clave de Gemini en texto plano **en el equipo del usuario**, no en el repositorio) y O-2 |
| 12 | Sin temporales, bin/obj ni dependencias innecesarias | **CUMPLE** | 0 carpetas `bin`, `obj`, `.vs`, `__pycache__`, `node_modules`, `.venv`, `*.user`, `*.log`. Se eliminaron 2 capturas sueltas que `recorrido.js` dejaba en la raíz y se corrigió el script (sección 6). `ml/datos/muestras.npz` (2,2 MB, se regenera) queda excluido por `.gitignore`. Los 4 paquetes NuGet se usan; las capas no tienen dependencias indebidas (S13-S16) |
| 13 | Licencias y atribuciones | **CUMPLE CON OBSERVACIONES** | `AUTHORS.md` (equipo y repositorio de origen) y `THIRD-PARTY-NOTICES.md` (paquetes NuGet y, ahora, herramientas de desarrollo con su licencia). No se modificó ninguna atribución existente. No hay líneas de coautoría ni firmas añadidas en ningún archivo. **Falta que el equipo elija la licencia del proyecto**: no hay archivo `LICENSE` |

## 2. Configuración Git del repositorio nuevo

| Aspecto | Estado |
|---|---|
| Historial | **No hay carpeta `.git`**: no se copió el historial del repositorio anterior y no se inicializó ninguno. El equipo lo crea al publicar |
| `.gitignore` | Plantilla de Visual Studio más reglas del proyecto. Excluye `bin/`, `obj/`, `.vs/`, `*.user`, `ml/.venv/`, `ml/datos/muestras.npz`, `__pycache__/`, `.env`, `*.local.json`, `clave-gemini.txt` y bases `*.mdf`/`*.ldf` |
| `.gitattributes` | `* text=auto` y, **agregado en esta auditoría**, `binary` para `*.onnx`, `*.pt`, `*.npz`, `*.png`, `*.jpg`, `*.docx` y `*.pdf`, y `eol=lf` para `*.sh`. Así Git no altera el modelo ni las imágenes con conversiones de fin de línea |
| Prueba en seco | En una copia temporal: `git init` y `git add -A --dry-run` → **233 archivos** se agregarían. El único excluido es `ml/datos/muestras.npz`. `git check-attr` confirma `demanda_mlp.onnx: binary`. La copia se borró; no se hizo ningún commit |
| Identidad | Este entorno tiene una identidad Git propia, que **no** debe usarse para el proyecto. Al no haber repositorio, no queda ninguna configuración local. Cada integrante debe confirmar con su `user.name` y `user.email` |
| Coautoría | No se agregaron líneas `Co-authored-by` ni firmas en ningún archivo |

## 3. Archivos definitivos

233 archivos (7,3 MB) en el momento de esta auditoría; la lista vigente del cierre está en el mismo archivo. La lista completa está en
[evidencias/revision/archivos_publicacion.txt](../evidencias/revision/archivos_publicacion.txt).

| Carpeta | Archivos | Contenido |
|---|---:|---|
| raíz | 7 | `README.md`, `AUTHORS.md`, `THIRD-PARTY-NOTICES.md`, `FerroSuite.slnx`, `Directory.Build.props`, `.gitignore`, `.gitattributes` |
| `config/` | 1 | `appsettings.json` (sin credenciales) |
| `src/` | 112 | Cuatro capas en 5 proyectos; `frontend/` con 41 archivos |
| `database/` | 6 | Migraciones V001-V003, demostración, base v1 para pruebas, README |
| `ml/` | 38 | Scripts, paquete `ferrosuite_demanda/`, datos sintéticos con su ficha, resultados (métricas, gráficas, pesos `modelo_pytorch.pt` de 26 KB) |
| `modelos/` | 4 | `demanda_mlp.onnx`, normalización, casos de referencia, ficha |
| `tests/` | 20 | Pruebas en memoria y SQL, herramienta de servidor, recorridos en navegador, scripts de revisión |
| `docs/` | 45 | Documentación por etapa, informes, evidencias, figuras e histórico |

## 4. Estado de compilación y pruebas

| Qué | Resultado | Registro |
|---|---|---|
| Compilación: Dominio, Aplicación, Infraestructura, Comunicación, Pruebas | 0 errores, 0 advertencias | `01_compilacion.txt` |
| Compilación: código de la ventana (WinForms de Mono, Debug y Release) | 0 errores, 0 advertencias | `01_compilacion.txt` |
| Compilación: `FerroSuite.slnx` completa con los paquetes reales | **Pendiente (Windows)** | `01b_compilacion_solucion_completa.txt` |
| Pruebas en memoria | **143 de 143** | `02_pruebas_en_memoria.txt` |
| Recorrido general en navegador | **28 de 28** | `03_recorrido_general.txt` |
| Pantalla de IA (con modelo / sin modelo) | **17 de 17 / 3 de 3** | `04_…`, `05_…` |
| Revisión integral en navegador | **26 de 29** (fallan R5 → D-01, R24 → D-04, R26 → D-03) | `06_revision_integral.txt` |
| Predictor con falla | **4 de 4** | `07_predictor_con_falla.txt` |
| Sin internet (red aislada) | **143/143 · 17/17 · 26/29** | `sin_red/` |
| Verificaciones estáticas | **17 de 19** (fallan S7 → D-01/D-02, S9 → D-06) | `11_verificaciones_estaticas.txt` |
| Suite SQL Server (`-- --bd`) | **Pendiente (Windows): 0 de 51 ejecutadas** | Se generará en `tests/resultados/` |

Los registros son de esta auditoría (18-nov-2026), salvo `01b`, que repite el intento de compilación completa.

## 5. Errores conocidos

| ID | Severidad | Error | ¿Bloquea la publicación? |
|---|---|---|---|
| D-03 | Alta | El borrador de orden de la pantalla de IA puede crear una orden duplicada con un segundo clic | **Se recomienda corregirlo antes de publicar como versión final** |
| D-04 | Media | Si el backend no responde al abrir una pantalla, se muestra «Sin acceso» en vez de «No hay comunicación» | Recomendable corregirlo |
| D-01 | Media | Falta «Editar» en la pantalla de proveedores (el backend sí lo permite) | No |
| D-02 | Media | Faltan pantallas para bodegas y categorías, y para editar el nombre o el rol de los usuarios | No (alcance) |
| D-05 | Media | La clave de Gemini se guarda en texto plano en el equipo del usuario | No (Gemini es opcional) |
| D-06 | Baja | Identificadores de prueba repetidos | No |
| D-07 | Baja | Los botones de las filas no se desactivan mientras responde el backend (el backend evita el duplicado) | No |
| D-08 | Baja | Si la base no se puede preparar al arrancar, se muestra el mensaje técnico de la excepción | No |

**Observaciones:**
- **O-1:** no hay reportes exportables.
- **O-2:** el `.onnx` guarda en sus metadatos la ruta de la carpeta donde se entrenó. No es información privada, pero cambia la huella si se reentrena en otra ruta.
- **O-3:** el pie de las pantallas enlaza a `github.com/guachulol0345896-pixel/FerroSuite-erp`; hay que confirmar o cambiar la URL al publicar.

Cómo reproducir cada defecto: [informe_pruebas.md](informe_pruebas.md#5-defectos-pendientes).

## 6. Evidencias necesarias para cerrar la verificación

Las debe generar un integrante en Windows 10/11 con Visual Studio 2026, el componente SQL Server Express LocalDB y WebView2 Runtime:

| Evidencia | Cómo | Resultado esperado | Dónde guardarla |
|---|---|---|---|
| E-W1 Compilación completa | `dotnet build FerroSuite.slnx -c Release > compilacion_windows.txt` | 0 errores; anotar las advertencias | `evidencias/revision/` |
| E-W2 Pruebas en memoria en Windows | `dotnet run --project tests/FerroSuite.Pruebas` | `Total: 143 pasan, 0 fallan` | `evidencias/revision/` |
| E-W3 Persistencia en SQL Server | `sqllocaldb start MSSQLLocalDB` y `dotnet run --project tests/FerroSuite.Pruebas -- --bd` | `Total: 51 pasan, 0 fallan` | `tests/resultados/resultados_bd_<fecha>.txt` (se crea solo) |
| E-W4 Arranque y uso de la ventana | F5 → crear el administrador → ingresar → producto → movimiento → orden (crear, aprobar, recibir) → Inteligencia artificial → cerrar y volver a abrir (los datos siguen) | Todo funciona y persiste | Capturas en `evidencias/windows/` |
| E-W5 Sin internet en Windows | Repetir E-W4 con la red desconectada | Igual; el asistente responde «con reglas» | Capturas |
| E-W6 Manejo de errores | *Opciones → Simular caída de la base de datos* y *Simular error de comunicación* | Mensajes claros; el programa sigue abierto | Capturas |

## 7. Instrucciones de ejecución

Resumen; el detalle está en el [README](../README.md).
1. Instalar Visual Studio 2026 (carga *Desarrollo de escritorio de .NET*) y el componente **SQL Server Express LocalDB**.
2. Abrir `FerroSuite.slnx`, establecer `FerroSuite.Presentacion.Escritorio` como proyecto de inicio y pulsar **F5**. La base se crea sola.
3. Crear el administrador cuando el programa lo pida (no hay claves por defecto) y crear los demás usuarios desde *Usuarios*.
4. Pruebas: `dotnet run --project tests/FerroSuite.Pruebas` y `dotnet run --project tests/FerroSuite.Pruebas -- --bd`.
5. Reentrenar la red (opcional), con Python 3.12 o 3.13:
   ```bat
   pip install -r ml/requirements.txt
   python ml/ejecutar_todo.py
   ```

## 8. Cambios de esta auditoría y pendientes de publicación

### Cambios hechos en esta auditoría

Ninguno toca el código del programa.

| Archivo | Cambio | Motivo |
|---|---|---|
| `.gitattributes` | Reglas `binary` y `eol=lf` | Proteger el modelo, las imágenes y los documentos al publicar |
| `FerroSuite.slnx` | Se reemplazaron 2 «elementos de solución» que ya no existían por los scripts SQL y documentos actuales | Referencias rotas en Visual Studio |
| `tests/pantallas/recorrido.js` | Las capturas auxiliares van a la carpeta temporal (o a `CAPTURAS`), no a la raíz del repositorio | Dejaba archivos temporales en el repositorio |
| `THIRD-PARTY-NOTICES.md` | Sección de herramientas de desarrollo y sus licencias | Atribución completa |
| `docs/arquitectura.md` | Paquetes de Infraestructura (Identity, OnnxRuntime), 41 archivos de `frontend`, archivos de `modelos` | No coincidía con los `.csproj` |
| `ml/README.md` | Python 3.12 o 3.13 (no 3.11); duración del flujo | 3.11 no instala `numpy` 2.5.3 |
| `docs/informe_pruebas.md` | Visual Studio 2026 (no 2022) para .NET 10 | Requisito de .NET 10 |
| `docs/seguridad.md`, `docs/pronostico.md`, `docs/pantalla_ia.md` | Recuento actual (143) o cifra marcada como «al cerrar la etapa» | Evitar contradicciones |
| `docs/README.md`, `README.md` | Enlace a este informe; se quitó del README el punto del `.slnx` (ya corregido) | Índice al día |
| `evidencias/` | Registros y capturas regenerados en esta auditoría | Evidencia fechada |
| `docs/auditoria_final.md`, `evidencias/revision/archivos_publicacion.txt` | Nuevos | Este informe y la lista de archivos |

### Pendiente antes o durante la publicación (decisiones y tareas del equipo)

1. **Generar las evidencias E-W1 a E-W6** en Windows y actualizar el estado de los puntos 1, 2 y 4.
2. **Decidir si se corrigen D-03 y D-04** antes de publicar (recomendado). Si se corrigen, repetir `tests/pantallas/revision.js`.
3. **Elegir la licencia** del proyecto y agregar `LICENSE`.
4. **Confirmar la URL del repositorio nuevo** y actualizar `frontend/js/components/pie.js` si cambia.
5. **Publicar desde la cuenta de cada integrante.** Ejemplo, desde la carpeta del proyecto:
   ```bat
   git init -b main
   git config user.name "Nombre del integrante"
   git config user.email "correo@del.integrante"
   git add -A
   git status
   git commit -m "FerroSuite 0.5.0"
   git remote add origin <URL del repositorio nuevo>
   git push -u origin main
   ```
   Revisar con `git status` que se agregan 233 archivos (sin `bin/`, `obj/` ni `muestras.npz`). No se deben agregar líneas de coautoría que el equipo no haya decidido.
6. Opcional: crear la etiqueta `v0.5.0` en el repositorio publicado una vez completadas las evidencias de Windows.
