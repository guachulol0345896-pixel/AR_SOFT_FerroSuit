# Informe final de FerroSuite 0.5.0 (para el revisor final)

> **Actualizado tras [CORRECCIONES_FINALES.md](CORRECCIONES_FINALES.md) (18-nov-2026):** cifras de pruebas al día; D-02, D-05, D-07 y D-08 de la sección 11 quedaron corregidos (ver ese archivo).

**Fecha:** 18 de noviembre de 2026.

**Entorno de verificación:** Linux con .NET SDK 10.0.115 y Python 3.13. No hay Windows ni SQL Server, ni acceso a
nuget.org. Las pantallas se probaron en Chromium sobre el backend C# real.

**Regla de este informe:** **VERIFICADO** = ejecutado aquí y correcto · **FALLÓ** = ejecutado e incorrecto ·
**NO PROBADO** = no se pudo ejecutar en este entorno. Todos los datos son sintéticos.

## Estado de cada componente

| Componente | Estado | Evidencia (`evidencias/`) |
|---|---|---|
| Compilación de Dominio, Aplicación, Infraestructura, Comunicación y Pruebas | **VERIFICADO** (0 errores, 0 advertencias) | `revision/01_compilacion.txt` |
| Compilación del código de la ventana (WinForms de Mono, Debug y Release) | **VERIFICADO** (solo compila) | `revision/01_compilacion.txt` |
| Compilación de `FerroSuite.slnx` completa con los paquetes NuGet reales | **NO PROBADO** (requiere Windows) | `revision/01b_compilacion_solucion_completa.txt` |
| Programa de escritorio en ejecución (Windows Forms + WebView2) | **NO PROBADO** | — |
| Inicio de sesión, bloqueo, vencimiento, cambio obligatorio de clave | **VERIFICADO** | `02` (A1-A9, U1-U8), `03` (E1, E2, E21, E22), `06` (R1, R2) |
| Permisos por rol en el backend (37 acciones × 4 roles + sin sesión) | **VERIFICADO** | `02` (MZ, D1), `03` (E3-E13, E19, E28) |
| Administrador: productos, proveedores (incluida la edición) y usuarios | **VERIFICADO** | `06` (R3-R8, R20-R22), `02` (P, V, U) |
| Vendedor: consulta de stock, ventas y devoluciones | **VERIFICADO** | `03` (E3-E8), `02` (L1, L2) |
| Bodega: entradas, ajustes, órdenes de compra y recepción | **VERIFICADO** | `03` (E9-E12), `06` (R9-R16) |
| Contador: consulta de valores, órdenes y pagos | **VERIFICADO** | `03` (E19, E20), `02` (L4, O9) |
| Botones y formularios conectados con el backend (incluidas bodegas, categorías y edición de usuarios) | **VERIFICADO** | `11` (S6, S7), `06` (R30-R35) |
| Persistencia en SQL Server LocalDB (54 pruebas `--bd`) | **NO PROBADO** | — |
| Misma lógica con el repositorio en memoria (transacciones, concurrencia, integridad) | **VERIFICADO** | `02` (M, O, X1, X2) |
| Red neuronal PyTorch: entrenamiento reproducible | **VERIFICADO** | `modelo/reentrenamiento_reproduccion.txt`, `modelo/reproducibilidad.json` |
| Modelo ONNX y parámetros de normalización | **VERIFICADO** (huella SHA-256 coincide) | `revision/01_compilacion.txt`, `02` (PI1) |
| C# carga y ejecuta el modelo con ONNX Runtime | **VERIFICADO** (biblioteca nativa 1.29.0; el paquete NuGet oficial queda NO PROBADO) | `02` (PI1-PI3) |
| Predicciones con datos del inventario, riesgo y recomendación | **VERIFICADO** | `02` (PU, PD), `04` (IA3-IA6) |
| Resultados visibles en FerroSuite (pantallas *Inteligencia artificial* y *Pronósticos*) | **VERIFICADO** en Chromium | capturas `ia_*.png`, `03` (E24-E27) |
| Sin dependencia de Gemini ni de APIs externas para predecir | **VERIFICADO** (todo pasa sin red) | `revision/sin_red/` |
| Respaldo con reglas de inventario cuando no hay datos suficientes o falla el modelo | **VERIFICADO** | `05` (IN1-IN3), `07` (RP1-RP4), `02` (PU5-PU8) |
| Manejo de errores (base caída, backend sin respuesta, predictor con falla) | **VERIFICADO** | `06` (R23-R25), `07`, `02` (W4, CM5) |
| Sin claves ni secretos en el repositorio | **VERIFICADO** | `11` (S10, S10b, S11) |

## 1. Versión terminada

FerroSuite 0.5.0 es un ERP de **escritorio para Windows** para ferreterías pyme. Cubre inventario por bodega, proveedores,
órdenes de compra, usuarios con cuatro roles y auditoría.

Incluye una **red neuronal propia** (PyTorch → ONNX → C#). Pronostica la demanda de cada producto y, con el stock y el
tiempo de entrega del proveedor, calcula el riesgo de agotamiento y cuánto pedir. Funciona sin internet.

## 2. Estructura de carpetas

```text
FerroSuite/
├── INFORME_FINAL_FERROSUITE.md, README.md, AUTHORS.md, THIRD-PARTY-NOTICES.md, FerroSuite.slnx, Directory.Build.props
├── config/          appsettings.json (sin credenciales)
├── src/             Dominio · Aplicacion · Infraestructura · Presentacion.Comunicacion · Presentacion.Escritorio (frontend/)
├── database/        migraciones V001-V003, demo (sintética), base v1 para pruebas de migración
├── ml/              entrenamiento PyTorch (scripts, paquete ferrosuite_demanda, datos sintéticos, resultados)
├── modelos/         demanda_mlp.onnx, demanda_mlp_normalizacion.json, casos de referencia, ficha
├── tests/           FerroSuite.Pruebas (154 en memoria + 54 SQL), pantallas/ (Playwright), revision/, resultados/
├── evidencias/      registros de compilación y pruebas, capturas, resultados del modelo (ver evidencias/README.md)
└── docs/            documentación por etapa, informes, Documento Maestro final (PDF), histórico
```

## 3. Tecnologías

| Parte | Tecnología |
|---|---|
| Lenguaje y plataforma | C# 14 sobre .NET 10 |
| Ventana | Windows Forms + WebView2 1.0.3967.48 |
| Pantallas | HTML, CSS y JavaScript, sin recursos de internet |
| Base de datos | SQL Server Express LocalDB (`Microsoft.Data.SqlClient` 6.0.2) |
| Claves | `PasswordHasher` de ASP.NET Core Identity 10.0.0 |
| Red neuronal | PyTorch 2.15.0, ONNX opset 18, `Microsoft.ML.OnnxRuntime` 1.29.0 |
| Asistente opcional | Gemini, con respaldo de reglas |

## 4. Funcionalidades implementadas

- Inventario: productos, categorías y bodegas en consulta; entradas, salidas y ajustes con responsable; historial.
- Proveedores: alta, edición, desactivar y activar; NIT con dígito de verificación.
- Órdenes de compra: crear, aprobar, rechazar, recibir y pagar.
- Usuarios y roles: crear, activar, restablecer clave, desbloquear; auditoría.
- Pronóstico neuronal: pantalla *Inteligencia artificial* (por producto) y tabla *Pronósticos* (todos los productos).
- Pantallas de consulta: resúmenes, historial, auditoría y pronósticos.

## 5. Componentes de inteligencia artificial

**Python (entrenamiento):**
- `ml/generar_datos_sinteticos.py`, `preparar_datos.py`, `entrenar.py`, `evaluar.py`, `exportar_onnx.py`, `exportar_casos_prueba.py`, `verificar_preparacion.py`.
- `ml/ejecutar_todo.py` ejecuta el flujo completo.
- El paquete `ml/ferrosuite_demanda/` contiene la red `MlpDemanda` (48 → 64 → 32 → 1).

**C# (ejecución en el programa):**

| Capa | Componentes |
|---|---|
| Aplicación (`IA/Pronostico/`) | `EspecificacionModelo`, `CalendarioFestivos`, `SerieDemanda`, `PreparadorCaracteristicas`, `PronosticoServicio`; puerto `IPronosticadorDemanda` |
| Infraestructura | `PronosticadorOnnx` (ONNX Runtime) y `RepositorioInventarioSql.ObtenerHistorialVentas` |
| Presentación | Acciones `pronosticos.reposicion` y `pronosticos.producto`; `ia.html` y `pronosticos.html` |

**Modelo:**
- Entradas: 28 días de ventas, resúmenes, calendario y categoría (48 en total) y una escala.
- Salida: consumo diario promedio de los próximos 7 días.

**Respaldo:** se usa la regla «vendido en 30 días ÷ 30» cuando:
- hay menos de 30 días de historia o menos de 14 de los últimos 28 días tienen dato;
- el modelo no está disponible o falla.

## 6. Ubicación del modelo ONNX

| Archivo | Contenido |
|---|---|
| `modelos/demanda_mlp.onnx` | El modelo. SHA-256 `08eb91cf8e254a8a76d087096909b287e1dbbd9fe9209e6ed05a56745dce5cce` |
| `modelos/demanda_mlp_normalizacion.json` | Orden de las 48 entradas, medias y desviaciones, parámetros de ventana y huella |

El proyecto de escritorio copia ambos a `<salida>\modelos\`.

## 7. Resultados del entrenamiento y evaluación (datos SINTÉTICOS)

Prueba cronológica de 3 418 muestras (`ml/resultados/metricas.json`):

| Método | MAE | RMSE |
|---|---:|---:|
| Red MLP | 2,427 | 6,300 |
| Promedio móvil de 28 días | 2,498 | 6,383 |
| Regla actual (30 días) | 2,674 | 6,797 |

- La red mejora la regla actual en un 9,2 % de MAE.
- ONNX frente a PyTorch: diferencia máxima de 7,6·10⁻⁶.
- C# frente a Python: diferencia 0 en 35 casos.
- Recorrido SINT-001: 3,61 por día (Python 3,6096) → 11,1 días → «Pedir ahora», 61 unidades.

**Estas cifras no demuestran el desempeño con datos reales.**

## 8. Estado de la base de datos

**Implementado:**
- 12 tablas con claves foráneas, CHECK y UNIQUE.
- Migraciones V001-V003 aplicadas automáticamente y registradas en `VersionEsquema`.
- Disparadores que impiden modificar movimientos y auditoría.
- Transacciones y control de concurrencia.
- Consultas parametrizadas.
- Bases Real y Demo separadas.
- Conexión con la cuenta de Windows (sin contraseñas).

Las **54 pruebas contra SQL Server** (integridad, transacciones, concurrencia, reapertura, migración) están escritas y
**NO PROBADAS**: requieren Windows con LocalDB.

## 9. Resultados de compilación y pruebas

| Prueba | Resultado |
|---|---|
| Compilación de las capas y de la ventana | 0 errores, 0 advertencias |
| Pruebas en memoria | **154/154** (también sin internet) |
| Recorrido general | **28/28** |
| Pantalla de IA (con modelo / sin modelo) | **17/17 / 3/3** |
| Revisión integral | **35/35** |
| Mensajes manipulados al Puente | **16/16** |
| Equivalencia PyTorch ↔ ONNX ↔ C# | **6/6** |
| Predictor con falla | **4/4** |
| Verificaciones estáticas | **19/19** |
| SQL Server (`--bd`) | **NO PROBADO** |

## 10. Problemas corregidos en el cierre

Se corrigieron y se repitieron todas las pruebas:

| ID | Corrección | Comprobado por |
|---|---|---|
| D-03 | El borrador de orden de la pantalla de IA ya no puede crear una orden duplicada: el panel se cierra al crearla (`js/controllers/ia.js`) | R26: 1 orden; IA13 |
| D-04 | Si el backend no responde, las pantallas avisan «No hay comunicación con el backend» con «Reintentar», en lugar de «Sin acceso» (`js/main.js`) | R24 |
| D-01 | Botón «Editar» en Proveedores; el NIT no se cambia (`proveedores.js`, `proveedoresService.js`) | R5 |
| D-06 | Identificadores de prueba únicos: asistente AS1-AS5, comunicación CM1-CM6, E26b | S9 |

Antes, en la auditoría final, también se corrigieron:
- referencias rotas en `FerroSuite.slnx`;
- capturas temporales en la raíz;
- `.gitattributes` para archivos binarios;
- documentación desactualizada;
- avisos de terceros.

Además:
- Las evidencias se movieron a `evidencias/`.
- Se preparó `docs/Documento_Maestro_FerroSuite_final.pdf`: fe de erratas técnica más el Documento Maestro original sin alterar.

## 11. Errores y limitaciones pendientes

**Defectos:**
- ~~**D-02 (media):** sin pantallas para crear o editar bodegas y categorías, ni para editar el nombre o el rol de un usuario.~~ **Corregido** (ver CORRECCIONES_FINALES.md).
- ~~**D-05 (media):** la clave opcional de Gemini se guarda en texto plano en el equipo del usuario.~~ **Corregido** (ver CORRECCIONES_FINALES.md).
- ~~**D-07 (baja):** los botones de las filas no se desactivan mientras responde el backend. El backend evita los duplicados.~~ **Corregido** (ver CORRECCIONES_FINALES.md).
- ~~**D-08 (baja):** si la base no se puede preparar al arrancar, se muestra el mensaje técnico de la excepción.~~ **Corregido** (ver CORRECCIONES_FINALES.md).

**Limitaciones:**
- **NO PROBADO:** la ventana en Windows, la compilación con los paquetes NuGet reales y SQL Server.
- La red está entrenada con datos sintéticos.
- No hay reportes exportables.
- El `.onnx` guarda en sus metadatos la ruta donde se entrenó.
- Falta elegir la licencia del proyecto y confirmar la URL del repositorio (pie de las pantallas).

## 12. Instrucciones exactas para ejecutar el sistema

1. **Preparar Windows 10/11:**
   - Visual Studio 2026, carga *Desarrollo de escritorio de .NET*.
   - Componente individual **SQL Server Express LocalDB**.
   - WebView2 Runtime (viene con Windows 11).
2. **Iniciar LocalDB:** `sqllocaldb start MSSQLLocalDB`.
3. **Abrir el programa:** abrir `FerroSuite.slnx` → clic derecho en `FerroSuite.Presentacion.Escritorio` → *Establecer como proyecto de inicio* → **F5**. La base `FerroSuite` se crea sola.
4. **Primer uso:** crear el administrador cuando el programa lo pida (no hay claves por defecto) y crear los demás usuarios desde *Usuarios*.
5. **Datos de demostración** (opcional): en `config/appsettings.json`, `"Modo": "Demo"`. Luego, *Opciones → Generar claves de las cuentas de demostración…*.
6. **Pruebas:**
   ```bat
   dotnet run --project tests/FerroSuite.Pruebas
   dotnet run --project tests/FerroSuite.Pruebas -- --bd
   ```
   Esperado: 154 y 54 pasan. La segunda guarda su resultado en `tests/resultados/`.
7. **Reentrenar la red** (opcional, Python 3.12 o 3.13):
   ```bat
   pip install -r ml/requirements.txt
   python ml/ejecutar_todo.py
   ```
8. **Evidencias que faltan** (para cerrar los NO PROBADO): guardar en `evidencias/windows/` la salida de la compilación, la de `--bd` y capturas de la ventana (ingreso, inventario, orden, *Inteligencia artificial*, sin internet). Pasos en `docs/auditoria_final.md` §6.
