# Resultados de compilación y pruebas

## Correcciones finales (18 de noviembre de 2026)

Ver [CORRECCIONES_FINALES.md](../CORRECCIONES_FINALES.md).

| Qué | Resultado |
|---|---|
| Compilación de las capas y de la ventana (Debug y Release) | 0 errores, 0 advertencias |
| Pruebas en memoria | 154/154 (también sin internet) |
| Pantallas | 28/28, 17/17, 3/3, 35/35, 4/4; mensajes manipulados 16/16 |
| Verificaciones estáticas | 19/19 |
| Equivalencia PyTorch ↔ ONNX ↔ C# | 6/6 |
| SQL Server (54 pruebas) y ventana en Windows | **No probados** |

---

## Revisión integral (18 de noviembre de 2026)

Ver [informe_pruebas.md](informe_pruebas.md). En resumen: 143/143 pruebas en memoria (también sin internet), 28/28 y
20/20 en navegador, 26/29 en la revisión integral nueva y 17/19 verificaciones estáticas; 8 defectos registrados;
pendientes: compilación con paquetes reales, suite SQL Server (51) y arranque en Windows.

---

## Etapa 6: pantalla de Inteligencia Artificial (18 de noviembre de 2026)

| # | Qué | Resultado |
|---|---|---|
| 1 | Compilación de todos los proyectos y de la ventana (Debug y Release) | 0 errores, 0 advertencias |
| 2 | Pruebas en memoria | **142 de 142** (10 nuevas: PD1-PD10, detalle por producto con el modelo ONNX real) |
| 3 | Pantalla de IA en navegador (`tests/pantallas/pantalla_ia.js`) | **17 de 17** con el modelo; **3 de 3** sin el modelo (`--sin-modelo`) |
| 4 | Recorrido general (`recorrido.js`) | **28 de 28** |
| 5 | Recursos del ejecutable | 41 archivos de `frontend/` (nuevos: `ia.html`, `js/controllers/ia.js`, `js/components/graficaDemanda.js`) |
| 6 | Suite SQL Server (`-- --bd`, 51 pruebas con F4 nueva) | **No ejecutada** (sin SQL Server en este entorno) |
| 7 | Capturas | `evidencias/` (6 imágenes, Chromium, datos sintéticos) |

En Windows: `dotnet run --project tests/FerroSuite.Pruebas` → `Total: 142 pasan, 0 fallan`;
`-- --bd` → `Total: 51 pasan, 0 fallan`. Luego F5, ingresar como bodega o administrador y abrir *Inteligencia artificial*.

---

## Etapa 5: integración del modelo ONNX en C# (17 de noviembre de 2026)

| # | Qué | Resultado |
|---|---|---|
| 1 | Compilación de todos los proyectos y de la ventana (Debug y Release) | 0 errores, 0 advertencias (con el envoltorio de verificación de ONNX Runtime; ver `docs/pronostico.md` §6) |
| 2 | Pruebas en memoria | **132 de 132** (19 nuevas de pronóstico, con ONNX Runtime real dentro del proceso) |
| 3 | Equivalencia con Python | Entradas y salida del modelo idénticas en 35 casos; demanda diaria idéntica en 4 series; festivos 36/36 |
| 4 | Recorrido de pantallas (Playwright) | **28 de 28** |
| 5 | Recursos del ejecutable | 38 archivos de `frontend/`; `modelos/demanda_mlp.onnx` y `demanda_mlp_normalizacion.json` |
| 6 | Python (`ml/ejecutar_todo.py`) | Sin cambios en dataset, pesos, métricas ni ONNX (mismas huellas SHA-256) |
| 7 | Suite SQL Server (`-- --bd`, 50 pruebas con F1-F3) | **No ejecutada** (sin SQL Server en este entorno) |

En Windows: `dotnet run --project tests/FerroSuite.Pruebas` → `Total: 132 pasan, 0 fallan`;
`dotnet run --project tests/FerroSuite.Pruebas -- --bd` → `Total: 50 pasan, 0 fallan`. Luego F5, ingresar como administrador
o bodega y abrir *Pronósticos* (con la base de demostración hay 30 días de ventas: la historia mínima justa).

---

## Etapa 4: red neuronal de demanda (17 de noviembre de 2026)

Entorno: Linux, Python 3.13.18, PyTorch 2.15.0 (CPU), ONNX 1.23.2, ONNX Runtime 1.29.0. Todo se **ejecutó** aquí.

| # | Qué | Resultado |
|---|---|---|
| 1 | `python ml/ejecutar_todo.py` (datos → preparación → entrenamiento → evaluación → ONNX → verificaciones) | Termina sin errores en 27 s |
| 2 | Reproducibilidad | Tres ejecuciones completas: mismas huellas SHA-256 de dataset, pesos, predicciones y métricas |
| 3 | Comprobaciones de preparación (`verificar_preparacion.py`) | 6 de 6 correctas (sin fugas del futuro, normalización solo con entrenamiento, separación cronológica con margen, sin vacíos, dataset reproducible, lector de movimientos) |
| 4 | Prueba (datos SINTÉTICOS) | Red MLP MAE 2,427 / RMSE 6,300; regla actual 2,674 / 6,797; promedio de 28 días 2,498 / 6,383 |
| 5 | ONNX frente a PyTorch | Coherente: diferencia máxima 7,6·10⁻⁶ en 3 418 muestras |

No verificado: ejecución del ONNX desde C# (etapa siguiente) y entrenamiento con datos reales (no existen todavía).

---

## Etapa 3: autenticación, autorización y auditoría (17 de noviembre de 2026)

Mismo entorno que la etapa 2 (Linux, .NET SDK 10.0.115, sin NuGet, sin SQL Server, sin Windows).
`Microsoft.Extensions.Identity.Core` no se pudo descargar; para compilar y **ejecutar el `PasswordHasher` real** se usó la
misma biblioteca que trae el runtime de ASP.NET Core 10.0.15 instalado en el entorno (un archivo de MSBuild local cambia
el `PackageReference` por una referencia al framework solo durante la verificación; no se entrega).

| # | Qué | Resultado |
|---|---|---|
| 1 | Compilación de Dominio, Aplicación, Comunicación, Infraestructura y Pruebas | 0 errores, 0 advertencias |
| 2 | `VentanaPrincipal.cs` real (Debug y Release, contra WebView2 y Windows Forms como metadatos) | 0 errores, 0 advertencias |
| 3 | Pruebas en memoria (`dotnet run --project tests/FerroSuite.Pruebas`) | **113 de 113 pasan** (incluye la matriz rol × acción, 35 acciones × 4 roles y sin sesión, y el hash real de Identity) |
| 4 | Recorrido de pantallas con Playwright (`tests/pantallas/recorrido.js`) | **23 de 23 pasan**, sin errores de JavaScript |
| 5 | Recursos del ejecutable | 35 archivos de `frontend/`; V001, V002 y **V003** → `BaseDatos\migraciones\`; demo; `appsettings.json` |
| 6 | `V003__seguridad_y_auditoria.sql` con `sqlglot` (T-SQL) | Sin errores de sintaxis (salvo `CREATE TRIGGER`, que no soporta) |
| 7 | Suite contra SQL Server (`-- --bd`, **47 pruebas**) | Compila; **no ejecutada** (no hay SQL Server en este entorno) |

No verificado aquí: restauración del paquete `Microsoft.Extensions.Identity.Core` 10.0.0 desde NuGet, la ventana real con
WebView2, la migración V003 y la auditoría en SQL Server. En Windows:

```bash
dotnet run --project tests/FerroSuite.Pruebas            # Total: 113 pasan, 0 fallan
dotnet run --project tests/FerroSuite.Pruebas -- --bd    # Total: 47 pasan, 0 fallan  (deja el archivo en tests/resultados/)
```

Luego F5 en `FerroSuite.Presentacion.Escritorio`: crear el administrador, crear un usuario de cada rol en *Usuarios*,
ingresar con cada uno y comprobar que solo ve y puede hacer lo de su rol (tabla en `docs/seguridad.md`).

---

## Etapa 2: persistencia (18 de noviembre de 2026)

Entorno de verificación: Linux, .NET SDK 10.0.115. **Sin acceso a NuGet** y **sin SQL Server** (no se puede instalar ni
ejecutar en este entorno), sin Windows ni Gemini real. Cada resultado dice qué se comprobó y contra qué.

| # | Qué | Cómo | Resultado |
|---|---|---|---|
| 1 | Dominio, Aplicación, Comunicación | `dotnet build` | Compilan, 0 errores, 0 advertencias |
| 2 | Infraestructura y Pruebas | `dotnet build` con un paquete **sustituto local** de `Microsoft.Data.SqlClient` 6.0.2 que hereda de `System.Data.Common` igual que el real (misma API pública usada por el código; no conecta a nada; no se entrega) | Compilan, 0 errores, 0 advertencias |
| 3 | `VentanaPrincipal.cs` y `Program.cs` reales | Compilados contra los proyectos reales, las DLL reales de WebView2 y Windows Forms de Mono (como metadatos) | Compilan, 0 errores |
| 4 | Pruebas en memoria | `dotnet run --project tests/FerroSuite.Pruebas` | **84 de 84 pasan** |
| 5 | Pantallas + backend | Chromium (Playwright) con `FerroSuite.Pruebas servidor` en lugar de WebView2 (`tests/pantallas/recorrido.js`) | **22 de 22 pasan**, sin errores de JavaScript |
| 6 | Recursos del ejecutable | `dotnet msbuild -getItem` | 32 archivos de `frontend/`; `V001`, `V002` → `BaseDatos\migraciones\`; `datos_demo.sql` → `BaseDatos\demo\`; `appsettings.json` |
| 7 | Scripts SQL | Análisis sintáctico con `sqlglot` (dialecto T-SQL), lote por lote | Sin errores de sintaxis salvo construcciones que `sqlglot` no soporta (`THROW`, `CREATE TRIGGER`, `IF (subconsulta) <> (subconsulta)`); al quitarlas, los lotes analizan bien. **No reemplaza ejecutarlos en SQL Server.** |
| 8 | Suite contra SQL Server (`-- --bd`, 38 pruebas) | Compila; aquí falla al conectar (no hay SQL Server) y lo informa sin cerrarse | **No ejecutada.** Debe correrse en Windows |

### No verificado en este entorno

- **Ejecución contra SQL Server LocalDB**: migraciones V001/V002, restricciones, disparador, transacciones, concurrencia,
  persistencia entre procesos, migración de la base antigua y demostración. Todo eso lo comprueba la suite `--bd`.
- La ventana real (WinForms + WebView2) y el diálogo de creación del primer administrador.
- Compilación del proyecto de escritorio con el SDK de Windows y los paquetes NuGet reales.

### Cómo confirmarlo en Windows

1. Visual Studio 2026 → abrir `FerroSuite.slnx` → **Recompilar solución** (6 proyectos correctos).
2. Terminal en la carpeta del repositorio:
   ```bash
   dotnet run --project tests/FerroSuite.Pruebas            # debe terminar con: Total: 84 pasan, 0 fallan
   dotnet run --project tests/FerroSuite.Pruebas -- --bd    # debe terminar con: Total: 38 pasan, 0 fallan
   ```
   El segundo comando deja la evidencia en `tests/resultados/resultados_bd_<fecha>.txt`.
3. Proyecto de inicio `FerroSuite.Presentacion.Escritorio` → F5 → crear el administrador → ingresar → registrar un
   movimiento → cerrar el programa → abrirlo de nuevo e ingresar: el movimiento sigue ahí con el responsable.
4. Para la demostración: `"Modo": "Demo"` en `bin\Debug\net10.0-windows\appsettings.json` → F5 → el título dice
   DEMOSTRACIÓN y aparece *Opciones → Reiniciar base de demostración…*.

---

## Etapa 1: reorganización (registro histórico)

Fecha: 17 de noviembre de 2026. Entorno de verificación: Linux, .NET SDK 10.0.115. **Sin acceso a NuGet** (bloqueado por la red
del entorno) y sin Windows, SQL Server ni Gemini real. Por eso cada resultado indica exactamente qué se compiló y contra qué.

| # | Qué | Cómo | Resultado |
|---|---|---|---|
| 1 | `FerroSuite.Dominio` | `dotnet build` | Compila, 0 errores, 0 advertencias |
| 2 | `FerroSuite.Aplicacion` | `dotnet build` | Compila, 0 errores, 0 advertencias |
| 3 | `FerroSuite.Presentacion.Comunicacion` | `dotnet build` | Compila, 0 errores, 0 advertencias |
| 4 | `FerroSuite.Infraestructura` | `dotnet build` con un paquete **sustituto local** de `Microsoft.Data.SqlClient` 6.0.2 (misma API usada por el código, sin conexión real; no se entrega) | Compila, 0 errores, 0 advertencias |
| 5 | `FerroSuite.Pruebas` | `dotnet build` (igual que 4) | Compila, 0 errores, 0 advertencias |
| 6 | Pruebas automáticas | `dotnet run --project tests/FerroSuite.Pruebas` | **42 de 42 pasan** (código de salida 0) |
| 7 | `VentanaPrincipal.cs` y `Program.cs` reales | Compilados contra los proyectos **reales** de Comunicación e Infraestructura, las DLL reales de WebView2 (paquete PyPI `pywebview` 6.2.1) y Windows Forms de Mono solo como metadatos | Compila, 0 errores |
| 8 | Recursos del proyecto de escritorio | `dotnet msbuild -getItem` sobre `FerroSuite.Presentacion.Escritorio.csproj` | 32 archivos de `frontend/` → `frontend\`; 2 scripts → `BaseDatos\`; `appsettings.json` → raíz de salida; `modelos/*.onnx` (ninguno aún) |
| 9 | Pantallas + backend reorganizado | Chromium (Playwright) con la herramienta `FerroSuite.Pruebas servidor` en lugar de WebView2 | 14 de 14 recorridos correctos, sin errores de JavaScript |
| 10 | Dependencias entre capas | Búsqueda de `using` y `ProjectReference` | Dominio no depende de nada; Aplicación solo de Dominio; Comunicación solo de Aplicación; ninguna referencia a `FerroSuite.IA` |

## No verificado en este entorno

- Compilación del proyecto `FerroSuite.Presentacion.Escritorio` con el SDK de Windows Desktop y los paquetes NuGet reales
  (necesita Windows o acceso a NuGet). Se espera que compile en Visual Studio porque sus archivos compilaron contra las mismas
  API (punto 7) y sus recursos resuelven bien (punto 8), pero **debe confirmarse en Visual Studio**.
- Ejecución contra SQL Server LocalDB, la ventana real con WebView2 y Gemini real.

## Cómo confirmarlo en Windows

1. Abrir `FerroSuite.slnx` en Visual Studio 2026 → **Compilar → Recompilar solución**. Resultado esperado: 6 proyectos correctos.
2. Proyecto de inicio `FerroSuite.Pruebas` → F5 → debe terminar con `Total: 42 pasan, 0 fallan`.
3. Proyecto de inicio `FerroSuite.Presentacion.Escritorio` → F5 → se abre la ventana, se crea la base y se ven Inicio, Inventario, Proveedores y Órdenes.
4. Revisar que en `bin\Debug\net10.0-windows\` existan `appsettings.json`, `frontend\` y `BaseDatos\`.
