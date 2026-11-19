# FerroSuite

**FerroSuite** es un programa de escritorio para Windows que gestiona el inventario, los proveedores y las compras de
ferreterías pequeñas y medianas en Colombia. Incluye una red neuronal propia que pronostica la demanda y recomienda
cuándo y cuánto reponer.

Proyecto de Arquitectura de Software, sexto semestre de Ingeniería de Software, Universidad Manuela Beltrán (2026).
Autores en [AUTHORS.md](AUTHORS.md).

> **Estado de esta versión (0.5.0, noviembre de 2026).**
> - Completados: la persistencia, la seguridad, el pronóstico con la red neuronal y la pantalla de Inteligencia Artificial.
> - Comprobado: el backend y las pantallas con pruebas automáticas (154 de 154) y recorridos en navegador.
> - **Sin ejecutar todavía:** la ventana en Windows y las 54 pruebas contra SQL Server, porque se desarrolló en un entorno sin Windows ni SQL Server (ver [Estado de las funciones](#estado-de-las-funciones)).
> - Se corrigieron los defectos de la auditoría independiente (arranque de WebView2, clave de Gemini cifrada, CRUD de bodegas, categorías y usuarios, mensajes y botones): ver [CORRECCIONES_FINALES.md](CORRECCIONES_FINALES.md). Lo pendiente está en [Limitaciones conocidas](#17-limitaciones-conocidas).
> - Informe para el revisor final: [INFORME_FINAL_FERROSUITE.md](INFORME_FINAL_FERROSUITE.md). Evidencias en [evidencias/](evidencias/README.md).
> - Detalle completo en [docs/informe_pruebas.md](docs/informe_pruebas.md).

## Contenido

1. [Descripción](#1-descripción)
2. [Propósito](#2-propósito)
3. [Arquitectura de cuatro capas](#3-arquitectura-de-cuatro-capas)
4. [Tecnologías](#4-tecnologías)
5. [Requisitos del sistema](#5-requisitos-del-sistema)
6. [SQL Server LocalDB](#6-instalación-y-configuración-de-sql-server-localdb)
7. [Compilar y ejecutar en Visual Studio](#7-compilación-y-ejecución-desde-visual-studio)
8. [Roles](#8-roles-y-sus-funciones)
9. [Módulos](#9-módulos-implementados)
10. [La red neuronal](#10-la-red-neuronal)
11. [Entrenamiento y formato ONNX](#11-tecnología-de-entrenamiento-y-formato-onnx)
12. [Componentes C# del modelo](#12-componentes-c-que-ejecutan-el-modelo)
13. [Flujo del pronóstico](#13-flujo-entrada--inferencia--salida--uso)
14. [Reproducir el entrenamiento](#14-reproducir-el-entrenamiento)
15. [Ejemplo comprobado](#15-ejemplo-comprobado-de-pronóstico)
16. [Pruebas](#16-pruebas)
17. [Limitaciones conocidas](#17-limitaciones-conocidas)
18. [Estructura del repositorio](#18-estructura-del-repositorio)

## Estado de las funciones

| Función | Estado |
|---|---|
| Inventario: productos, categorías, bodegas, entradas, salidas y ajustes con responsable | **Completado.** El administrador crea, edita, activa y desactiva bodegas y categorías desde Inventario |
| Proveedores con NIT y dígito de verificación: alta, edición, desactivar y activar | **Completado** |
| Órdenes de compra: crear, aprobar, rechazar, recibir y pagar | **Completado** |
| Usuarios, roles, sesión, bloqueo, auditoría | **Completado** |
| Persistencia en SQL Server LocalDB con migraciones | **Completado en código.** Las 54 pruebas contra SQL Server están **pendientes de ejecutar** en Windows |
| Pronóstico con red neuronal (PyTorch → ONNX → C#) y recomendación de reposición | **Completado.** El modelo está entrenado con datos **sintéticos** |
| Pantalla de Inteligencia Artificial (gráfica, riesgo, borrador de orden) | **Completado** |
| Asistente con Gemini (opcional; sin clave o sin internet responde con reglas) | **Completado** |
| Funcionamiento sin internet | **Comprobado** en el backend y las pantallas; **pendiente** en Windows |
| Ventana de Windows (WinForms + WebView2) | **Compila; pendiente de ejecutar** en Windows |
| Reportes exportables (PDF, Excel) | **No implementado.** Hay pantallas de consulta (resúmenes, historial, auditoría, pronósticos) |

## 1. Descripción

FerroSuite es una ventana de **Windows Forms** que muestra pantallas HTML, CSS y JavaScript dentro de **WebView2**.
- Las pantallas no guardan nada: piden cada operación al backend en C# por mensajes con el formato `{id, accion, datos}`.
- El backend comprueba la sesión y el permiso, ejecuta la operación y guarda en **SQL Server LocalDB**.
- No abre puertos ni usa `localhost`: las pantallas se sirven desde `https://ferrosuite.example`, una dirección virtual que solo existe dentro de la ventana.
- El pronóstico de demanda lo calcula una red neuronal en formato **ONNX**, ejecutada en el mismo equipo con ONNX Runtime. No depende de Gemini ni de ningún servicio comercial.

## 2. Propósito

Reemplazar el control de inventario en hojas de cálculo de una ferretería pyme con:
- el stock por bodega, siempre trazable (cada cambio es un movimiento con su responsable);
- el ciclo de compra completo, con aprobación del dueño y plazo de pago;
- permisos según el trabajo de cada persona (administrador, bodega, mostrador, contador);
- un aviso anticipado de desabastecimiento: cuántos días le quedan a cada producto según la demanda pronosticada y el tiempo de entrega del proveedor, y cuánto pedir.

## 3. Arquitectura de cuatro capas

| Capa | Proyecto | Contenido |
|---|---|---|
| **Presentación** | `FerroSuite.Presentacion.Escritorio` (ejecutable `FerroSuite.exe`, `net10.0-windows`) | Ventana WinForms + WebView2, pantallas en `frontend/`, raíz de composición (arma todas las capas) |
| | `FerroSuite.Presentacion.Comunicacion` (`net10.0`) | `Puente`: recibe los mensajes y comprueba origen, tamaño, sesión y permiso (deniega por defecto). Responde `{id, ok, mensaje, datos, errores}` |
| **Aplicación** | `FerroSuite.Aplicacion` (`net10.0`) | Casos de uso por módulo, matriz de permisos, auditoría, pronóstico (`IA/Pronostico`) y **puertos** (interfaces) |
| **Dominio** | `FerroSuite.Dominio` (`net10.0`, sin dependencias) | Entidades (Producto, Categoría, Bodega, Movimiento, Proveedor, Orden de compra y su detalle, Usuario, Rol, evento de auditoría) y reglas de SKU y NIT |
| **Infraestructura** | `FerroSuite.Infraestructura` (`net10.0`) | Repositorios SQL Server, migraciones, hash de claves (Identity), `PronosticadorOnnx`, cliente de Gemini, lectura de `appsettings.json` |

```text
Presentacion.Escritorio ──► Presentacion.Comunicacion ──► Aplicacion ──► Dominio
          │                                                  ▲
          └──────────────────► Infraestructura ──────────────┘   (implementa los puertos de Aplicación)
```

Aplicación no conoce SQL Server, ONNX Runtime ni WebView2: los usa a través de sus puertos (`IRepositorio…`,
`IPronosticadorDemanda`, `IHasherClaves`, `IGeneradorTexto`, `IMantenimientoDatos`). Más detalle en
[docs/arquitectura.md](docs/arquitectura.md).

## 4. Tecnologías

| Parte | Tecnología |
|---|---|
| Lenguaje y plataforma | C# 14, .NET 10 |
| Ventana | Windows Forms + Microsoft Edge WebView2 (`Microsoft.Web.WebView2` 1.0.3967.48) |
| Pantallas | HTML, CSS y JavaScript sin librerías ni recursos de internet; gráfica en SVG propio |
| Base de datos | SQL Server Express LocalDB (`Microsoft.Data.SqlClient` 6.0.2), migraciones SQL versionadas |
| Claves | `PasswordHasher` de ASP.NET Core Identity (`Microsoft.Extensions.Identity.Core` 10.0.0): PBKDF2-HMAC-SHA512, 210 000 iteraciones |
| Red neuronal | Entrenamiento: Python 3.13, PyTorch 2.15.0. Exportación: ONNX (opset 18). Ejecución: `Microsoft.ML.OnnxRuntime` 1.29.0 |
| Asistente (opcional) | Google Gemini por HTTPS; sin clave o sin internet responde con reglas locales |
| Pruebas | Programa de consola propio (`tests/FerroSuite.Pruebas`); recorridos en navegador con Node.js + Playwright |

Las licencias de los paquetes están en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## 5. Requisitos del sistema

| Para | Requisito |
|---|---|
| Usar y compilar el programa | Windows 10 u 11 (x64) |
| | **Visual Studio 2026** (versión 18.0 o posterior), con la carga *Desarrollo de escritorio de .NET*. .NET 10 solo se puede usar desde Visual Studio 2026; Visual Studio 2022 no lo admite |
| | .NET 10 SDK (viene con Visual Studio 2026) |
| | SQL Server Express LocalDB (sección 6) |
| | Microsoft Edge WebView2 Runtime (viene con Windows 11 y con Windows 10 actualizado) |
| | Internet solo la primera vez, para que Visual Studio descargue los paquetes NuGet. Después funciona sin conexión |
| Reentrenar la red (opcional) | **Python 3.12 o 3.13** (probado con 3.13). Con 3.11 falla la instalación, porque `numpy` 2.5.3 no está disponible para esa versión |
| Pruebas en navegador (opcional) | Node.js 18 o superior y Playwright con Chromium |

## 6. Instalación y configuración de SQL Server LocalDB

1. **Instalar LocalDB** (una de estas dos opciones):
   - *Visual Studio Installer → Modificar → Componentes individuales →* **SQL Server Express LocalDB**.
   - El instalador de SQL Server Express de Microsoft, opción *Descargar medios → LocalDB* (`SqlLocalDB.msi`).
2. **Comprobar la instancia** en un símbolo del sistema:
   ```bat
   sqllocaldb info
   sqllocaldb start MSSQLLocalDB
   sqllocaldb info MSSQLLocalDB
   ```
   La lista debe incluir `MSSQLLocalDB` y su estado debe ser *Running* («En ejecución»).
3. **Configurar FerroSuite** en [config/appsettings.json](config/appsettings.json). El archivo se copia junto al ejecutable y no lleva claves:

   | Opción | Por defecto | Para qué |
   |---|---|---|
   | `BaseDatos.Servidor` | `(localdb)\\MSSQLLocalDB` | Otra instancia, si se usa una distinta |
   | `BaseDatos.Modo` | `Real` | `Demo` usa otra base con datos **sintéticos** (`database/demo/datos_demo.sql`) |
   | `BaseDatos.NombreBaseReal` y `NombreBaseDemo` | `FerroSuite`, `FerroSuiteDemo` | Deben ser distintos |
   | `BaseDatos.TiempoConexionSegundos` | `30` | La primera conexión a LocalDB puede tardar |
   | `Gemini.Habilitado`, `Modelo`, `TiempoEsperaSegundos` | `true`, `gemini-flash-latest`, `60` | El asistente opcional |

4. **No hay que crear la base a mano.** Al abrir, el programa crea la base si no existe y aplica en orden las migraciones
   [V001](database/migraciones/V001__esquema_inicial.sql), [V002](database/migraciones/V002__modelo_completo.sql) y
   [V003](database/migraciones/V003__seguridad_y_auditoria.sql), que quedan registradas en la tabla `VersionEsquema`.
   - La conexión usa la cuenta de Windows (`Integrated Security`): no hay usuario ni contraseña de SQL.
   - Para ver la base: Visual Studio → *Explorador de objetos de SQL Server* → `(localdb)\MSSQLLocalDB`.

Detalle del modelo de datos, las transacciones y la migración desde la versión anterior en [docs/persistencia.md](docs/persistencia.md).

## 7. Compilación y ejecución desde Visual Studio

1. Abrir `FerroSuite.slnx` en Visual Studio 2026.
2. Clic derecho en `FerroSuite.Presentacion.Escritorio` → **Establecer como proyecto de inicio**.
3. **F5.** La primera vez Visual Studio descarga los paquetes NuGet. El programa muestra «Preparando la base de datos…» y crea o actualiza la base.
4. **Primer uso:** si no existe ningún administrador, el programa pide crearlo (usuario, nombre y clave). **No hay claves por defecto.** Con ese usuario se ingresa y se crean los demás desde *Usuarios*.
5. El menú **Opciones** (solo el administrador) tiene:
   - *Clave de Gemini…*: la guarda **cifrada con DPAPI de Windows** en `%LocalAppData%\FerroSuite\clave-gemini.dat` (nunca en texto plano); también se puede usar la variable de entorno `GEMINI_API_KEY`. Con clave, el asistente envía a Google nombres de productos y proveedores, cantidades y costos estimados; el pronóstico neuronal no usa Gemini;
   - *Probar conexión con Gemini*;
   - *Simular error de comunicación* y *Simular caída de la base de datos*: para las pruebas de manejo de errores;
   - en modo `Demo`, *Generar claves de las cuentas de demostración…* y *Reiniciar base de demostración…*.

Desde la línea de comandos en Windows:

```bat
dotnet build FerroSuite.slnx -c Release
dotnet run --project src/FerroSuite.Presentacion.Escritorio
```

Junto al ejecutable se copian 50 archivos:
- `frontend\` (43 archivos);
- `BaseDatos\migraciones\` y `BaseDatos\demo\`;
- `appsettings.json`;
- `modelos\demanda_mlp.onnx` y `modelos\demanda_mlp_normalizacion.json`.

Si el modelo falta o no pasa su verificación, el programa sigue funcionando y el pronóstico usa la regla de respaldo.

> **Pendiente de verificación:** esta compilación y el arranque de la ventana no se han ejecutado en Windows (sección 16).

## 8. Roles y sus funciones

La matriz está en un solo lugar ([Permisos.cs](src/FerroSuite.Aplicacion/Seguridad/Permisos.cs)). La comprueban el
`Puente` y los servicios; ocultar botones en las pantallas es solo comodidad.

| Rol (código) | Puede | No puede |
|---|---|---|
| **Administrador** (`ADMIN`) | Todo: productos (crear, editar, eliminar), proveedores, movimientos, órdenes (crear, aprobar, rechazar, recibir, pagar), pronósticos, asistente, usuarios, auditoría, configuración y base de demostración | — |
| **Encargado de bodega** (`BODEGA`) | Ver inventario e historial, ajustes y entradas, ver proveedores y órdenes, **crear** órdenes, **recibir** mercancía, pronósticos y pantalla de IA, asistente. Ve el **precio unitario de compra** de cada producto (lo necesita para pedir) | Vender, crear o eliminar productos, aprobar o pagar órdenes, ver el valor total del inventario y las cuentas por pagar, usuarios |
| **Vendedor de mostrador** (`VENDEDOR`) | Ver el stock, registrar **ventas** y **devoluciones** | Todo lo demás; no ve costos |
| **Contador externo** (`CONTADOR`) | Ver inventario con valores, movimientos, proveedores y órdenes; **marcar órdenes pagadas**; recordatorios de pago | Mover inventario, crear o aprobar órdenes, pronósticos |

Otras medidas de seguridad:
- Claves con hash de Identity.
- La cuenta se bloquea 15 minutos después de 5 intentos fallidos.
- La sesión vence tras 30 minutos sin actividad o a las 12 horas.
- Las claves iniciales y las restablecidas obligan a cambiar la clave en el siguiente ingreso.
- La auditoría registra las operaciones sensibles y no guarda ninguna clave.

Detalle en [docs/seguridad.md](docs/seguridad.md).

## 9. Módulos implementados

| Módulo | Pantalla | Qué hace | Estado |
|---|---|---|---|
| Inicio | `index.html` | Resumen: productos, en riesgo, proveedores, órdenes pendientes | Completado |
| Inventario | `inventario.html` | Catálogo con filtros, alta y edición de productos, entradas, salidas y ajustes por bodega, historial, asistente; administración de bodegas y categorías (solo administrador) | Completado |
| Proveedores | `proveedores.html` | Alta, edición, desactivar y activar; NIT con dígito de verificación DIAN | Completado |
| Órdenes de compra | `ordenes.html` | Varias líneas, precio del momento, aprobar, rechazar, recibir (suma al stock), pagar (plazo de 15 días) | Completado |
| Inteligencia artificial | `ia.html` | Por producto: historial de 90 días, gráfica histórica y pronosticada, stock, demanda estimada, días hasta agotarse, entrega, riesgo, recomendación, fuente (red o respaldo), borrador de orden | Completado |
| Pronósticos | `pronosticos.html` | Tabla de reposición de todos los productos, ordenada por urgencia | Completado |
| Usuarios y auditoría | `usuarios.html` | Crear, editar nombre y rol, activar y desactivar, restablecer clave, desbloquear; consulta de auditoría | Completado |
| Ingreso | `login.html` | Ingreso y cambio obligatorio de clave | Completado |

## 10. La red neuronal

Una red **MLP pequeña** (48 → 64 → 32 → 1, ReLU, dropout 0,1, 5 249 parámetros) estima el **consumo diario promedio de
los próximos 7 días** de cada producto. Usa sus ventas de los últimos **28 días** y el calendario.

- **Entradas (48):**
  - las 28 ventas diarias, divididas por la escala del producto (promedio de la ventana + 0,05);
  - resúmenes de la ventana: promedios de 7 y 14 días, desviación, máximo, proporción de ceros y de faltantes, pendiente;
  - el logaritmo de la escala;
  - el calendario: día de la semana, mes y festivos colombianos de los días a pronosticar;
  - la categoría y si la unidad admite decimales.
- **Faltantes:** los días agotados o sin registro no cuentan como venta cero. Se imputan con la mediana de la ventana, y la proporción de faltantes es una entrada más.
- **Condiciones para usar la red:** al menos 30 días de historia y 14 de los últimos 28 con dato. Si no se cumplen, o si el modelo no está disponible o falla, se usa la **regla de respaldo** (vendido en 30 días ÷ 30) y la pantalla dice por qué.
- **Resultados con datos sintéticos** (prueba cronológica de 3 418 muestras):

  | Método | MAE | RMSE |
  |---|---:|---:|
  | Red MLP | **2,427** | **6,300** |
  | Promedio móvil de 28 días | 2,498 | 6,383 |
  | Regla de FerroSuite (30 días) | 2,674 | 6,797 |

  La red mejora la regla actual en un 9,2 % de MAE. Estas cifras **no** demuestran el desempeño con datos reales.

Diseño, métodos base y gráficas en [ml/README.md](ml/README.md). Ficha del modelo en [modelos/README.md](modelos/README.md).

## 11. Tecnología de entrenamiento y formato ONNX

- **Entrenamiento:**
  - PyTorch 2.15.0: AdamW, pérdida Huber, lotes de 256, parada temprana con paciencia de 25 épocas.
  - Separación **cronológica** de entrenamiento, validación y prueba, con 7 días de margen y sin fugas del futuro (verificado por `ml/verificar_preparacion.py`).
  - La normalización usa solo los datos de entrenamiento.
- **Datos:** sintéticos, reproducibles (semilla 20261117) y documentados en [ml/datos/FICHA_DATOS_SINTETICOS.md](ml/datos/FICHA_DATOS_SINTETICOS.md). FerroSuite aún no tiene historial real suficiente.
- **Exportación:**
  - `torch.onnx.export(..., dynamo=True, opset_version=18)` (requiere `onnxscript`) a [modelos/demanda_mlp.onnx](modelos/demanda_mlp.onnx).
  - El archivo incluye la normalización y un `max(0, ·)`, así que nunca da consumo negativo.
  - Entradas: `caracteristicas` (float32 `[N,48]`) y `escala` (float32 `[N,1]`). Salida: `consumo_diario` (float32 `[N,1]`).
  - [modelos/demanda_mlp_normalizacion.json](modelos/demanda_mlp_normalizacion.json) guarda el orden de las 48 entradas, los parámetros de ventana y la **huella SHA-256** del `.onnx`.
- **¿Por qué ONNX?** Es un formato abierto que C# ejecuta con ONNX Runtime en el mismo equipo, sin Python ni servicios externos. La diferencia entre ONNX y PyTorch es de 7,6·10⁻⁶ como máximo en 3 418 muestras.

## 12. Componentes C# que ejecutan el modelo

| Capa | Componente | Responsabilidad |
|---|---|---|
| Aplicación | [EspecificacionModelo](src/FerroSuite.Aplicacion/IA/Pronostico/EspecificacionModelo.cs) | Ventana 28, horizonte 7, historia mínima 30, mínimo 14 días con dato, nombres y orden de las 48 entradas |
| | [CalendarioFestivos](src/FerroSuite.Aplicacion/IA/Pronostico/CalendarioFestivos.cs) | Festivos de Colombia de cualquier año (Pascua y Ley Emiliani) |
| | [SerieDemanda](src/FerroSuite.Aplicacion/IA/Pronostico/SerieDemanda.cs) | Demanda diaria a partir de los movimientos: ventas − devoluciones; agotados y días sin historia quedan como faltantes |
| | [PreparadorCaracteristicas](src/FerroSuite.Aplicacion/IA/Pronostico/PreparadorCaracteristicas.cs) | Las 48 entradas y la escala, igual que en Python |
| | [PronosticoServicio](src/FerroSuite.Aplicacion/IA/Pronostico/PronosticoServicio.cs) | Orquesta el flujo, aplica el respaldo, calcula días, riesgo y cantidad, y arma la lista y el detalle por producto |
| | [IPronosticadorDemanda](src/FerroSuite.Aplicacion/Puertos/IPronosticadorDemanda.cs) | Puerto del modelo: estado y predicción por lotes |
| | [CalculadoraReposicion](src/FerroSuite.Aplicacion/IA/CalculadoraReposicion.cs) | Regla de cantidad sugerida y tiempo de entrega por defecto (3 días) |
| Infraestructura | [PronosticadorOnnx](src/FerroSuite.Infraestructura/IA/Prediccion/PronosticadorOnnx.cs) | Carga `modelos/demanda_mlp.onnx` con `Microsoft.ML.OnnxRuntime`; verifica huella, parámetros y grafo; ejecuta por lotes |
| | [RepositorioInventarioSql](src/FerroSuite.Infraestructura/Persistencia/RepositorioInventarioSql.cs) | `ObtenerHistorialVentas`: totales diarios por producto y primer movimiento (consultas parametrizadas) |
| Presentación | [Puente](src/FerroSuite.Presentacion.Comunicacion/Puente.cs) | Acciones `pronosticos.reposicion` y `pronosticos.producto` (permiso `pronosticos.ver`: administrador y bodega) |
| | `frontend/ia.html`, `js/controllers/ia.js`, `js/components/graficaDemanda.js`, `pronosticos.html` | Muestran lo que calcula el backend; JavaScript no calcula ningún pronóstico |

## 13. Flujo: entrada → inferencia → salida → uso

```text
Pantalla IA o Pronósticos ──► Puente: "pronosticos.producto" / "pronosticos.reposicion" (sesión y permiso)
  1. Entrada    RepositorioInventarioSql.ObtenerHistorialVentas → ventas, devoluciones y cambio de stock por día
                SerieDemanda → demanda diaria hasta ayer (día de corte); el stock se reconstruye hacia atrás
                PreparadorCaracteristicas → 48 entradas + escala de la ventana de 28 días
  2. Inferencia PronosticadorOnnx → ONNX Runtime (un lote para todos los productos) → consumo diario
                Si falta historia o el modelo no está disponible o falla → regla de respaldo (vendido en 30 días / 30)
  3. Salida     días hasta agotarse = stock / consumo; fecha estimada; demanda de los próximos 7 días
                urgencia y riesgo: Urgente → Alto (se agota antes de que llegue un pedido)
                                   Pedir ahora → Medio (stock en el mínimo o agotamiento antes de entrega + 3 días)
                                   Programar pedido → Bajo (antes de entrega + 7 días)
                                   Sin acción → Muy bajo
                cantidad = máx(consumo × (entrega + 15) + mínimo − stock, 2 × mínimo − stock) − lo ya pedido
  4. Uso        La pantalla muestra todo, con la fuente (red neuronal o regla de respaldo) y su motivo.
                "Generar borrador de orden" crea la orden en "Pendiente de aprobación"; nada se aprueba ni se envía solo.
```

Una falla del modelo nunca impide registrar movimientos, productos u órdenes. Detalle en
[docs/pronostico.md](docs/pronostico.md) y [docs/pantalla_ia.md](docs/pantalla_ia.md).

## 14. Reproducir el entrenamiento

En Windows, desde la raíz del repositorio:

```bat
py -3.13 -m venv .venv
.venv\Scripts\activate
pip install -r ml/requirements.txt
python ml/ejecutar_todo.py
```

En Linux o macOS: `python3.13 -m venv .venv` y `source .venv/bin/activate`.

`ejecutar_todo.py` hace estos pasos (cada uno se puede ejecutar por separado; ver [ml/README.md](ml/README.md)):
1. Genera los datos sintéticos.
2. Prepara las ventanas y verifica que no haya fugas.
3. Entrena.
4. Evalúa frente a los métodos base.
5. Exporta a ONNX y compara ONNX Runtime con PyTorch.
6. Exporta los casos de referencia que usan las pruebas de C#.

Tarda menos de un minuto en CPU. Escribe:
- `ml/resultados/` (métricas, gráficas, `reproducibilidad.json`);
- `modelos/demanda_mlp.onnx`, `demanda_mlp_normalizacion.json` y `demanda_mlp_casos_prueba.json`.

**Qué esperar.** Con las mismas versiones, el dataset, los pesos, las predicciones y las métricas salen idénticos: la
misma huella en `ml/resultados/reproducibilidad.json`. El `.onnx` también es idéntico byte a byte si se entrena en la
misma carpeta. En otra carpeta su SHA-256 cambia, porque el exportador guarda la ruta del archivo fuente en los
metadatos, pero los pesos y las salidas son iguales y el `.json` registra la huella nueva. Después de reentrenar, las
pruebas de C# (sección 16) comprueban que el programa sigue dando lo mismo que Python.

Para entrenar con **datos reales** (movimientos exportados de SQL Server), ver [ml/README.md](ml/README.md#con-datos-reales-de-ferrosuite).

## 15. Ejemplo comprobado de pronóstico

Producto sintético `SINT-001` (construcción, bulto). Día de corte 2024-12-24, con las ventas de sus últimos 30 días del
periodo de prueba. Stock 40, mínimo 10, proveedor con 10 días de entrega.

| Paso | Python (entrenamiento) | C# (programa, prueba PI3) |
|---|---|---|
| Consumo diario de la red | 3,6096 | **3,61** (misma salida de ONNX Runtime: diferencia 0) |
| Regla actual (30 días / 30) | 3,9 | 3,9 |
| Días hasta agotarse | — | 40 / 3,61 = **11,1 días** |
| Urgencia | — | **Pedir ahora** (11,1 ≤ entrega + 3 = 13) |
| Cantidad sugerida | — | máx(3,61 × 25 + 10 − 40, 2 × 10 − 40) = 60,25 → **61** |
| Orden | — | Se crea solo si el usuario la genera; queda **pendiente de aprobación** (PI4) |

Al final de `dotnet run --project tests/FerroSuite.Pruebas` aparece la medición:
`Recorrido SINT-001: red neuronal 3.61 por día (Python: 3.6096); regla 30 días 3.9 (Python: 3.9); stock 40 → 11.1 días; entrega 10; Pedir ahora; pedir 61`.

Los datos de entrada están en `modelos/demanda_mlp_casos_prueba.json` (sección `recorrido`). Capturas de la pantalla
de IA con productos sintéticos en [evidencias/](evidencias/README.md).

## 16. Pruebas

| Prueba | Comando | Dónde corre | Último resultado |
|---|---|---|---|
| Backend completo en memoria: dominio, permisos (43 acciones × 4 roles), sesión, CRUD, inventario, órdenes, concurrencia, errores, Gemini, ONNX real y equivalencia con Python | `dotnet run --project tests/FerroSuite.Pruebas` | Cualquier sistema con .NET 10 | **154 de 154** (también sin internet) |
| SQL Server: migraciones, restricciones, transacciones, concurrencia, persistencia, pronóstico | `dotnet run --project tests/FerroSuite.Pruebas -- --bd` | Windows con LocalDB | **Pendiente de ejecutar** (54 pruebas). Guarda el resultado en `tests/resultados/` |
| Pantallas en navegador con el backend real | Ver [tests/pantallas/README.md](tests/pantallas/README.md) | Node.js + Playwright | Recorrido general 28/28 · pantalla IA 17/17 y 3/3 sin modelo · revisión integral 35/35 · predictor con falla 4/4 · mensajes manipulados 16/16 |
| Verificaciones estáticas (rutas, claves, dependencias, duplicados) | `python tests/revision/verificaciones_estaticas.py` | Python 3 | 19/19 |
| Equivalencia PyTorch ↔ ONNX ↔ casos de C# (sin reentrenar) | `python tests/revision/equivalencia_modelo.py` | Python con `ml/requirements.txt` | 6/6 (3 418 muestras, diferencia máx. 7,6·10⁻⁶) |
| Datos y modelo de Python | `python ml/verificar_preparacion.py` | Python con `ml/requirements.txt` | Sin fugas, normalización y faltantes correctos |

Todas las pruebas usan **datos sintéticos**.
- Las claves de los usuarios de prueba se generan al azar en cada ejecución.
- La suite en memoria devuelve código 0 si todo pasa.
- `tests/revision/ejecutar_revision.sh` ejecuta todo y guarda los registros. Está pensado para el entorno de verificación en Linux; ver [docs/compilacion.md](docs/compilacion.md).

Informe completo con identificadores, entradas, resultados esperados y reales: [docs/informe_pruebas.md](docs/informe_pruebas.md).

**Para completar la verificación en Windows:**
1. `dotnet build FerroSuite.slnx -c Release`.
2. `sqllocaldb start MSSQLLocalDB`.
3. `dotnet run --project tests/FerroSuite.Pruebas -- --bd` (esperado: 54 pasan).
4. Las pruebas en memoria: `dotnet run --project tests/FerroSuite.Pruebas` (esperado: 154 pasan).
5. F5: crear el administrador, ingresar, registrar un movimiento, crear y aprobar una orden, abrir *Inteligencia artificial*.
6. Repetir con la red desconectada.

## 17. Limitaciones conocidas

**Del entorno y los datos**
- La red neuronal está entrenada con **datos sintéticos**: sirve para validar la integración, no para decidir compras reales. Hay que reentrenarla y evaluarla con el historial real.
- **No ejecutado en Windows:** la ventana, la compilación con los paquetes NuGet reales y las 54 pruebas con SQL Server. El código se desarrolló y probó en Linux, con las pantallas en Chromium y un repositorio en memoria.

**Defectos:** los de la auditoría independiente están corregidos o documentados en [CORRECCIONES_FINALES.md](CORRECCIONES_FINALES.md).
El cifrado DPAPI de la clave de Gemini y el nuevo arranque de WebView2 están escritos y compilan, pero **no se han
ejecutado en Windows**.

**Registro técnico:** los errores no previstos se registran, con su número de referencia, en
`%LocalAppData%\FerroSuite\registros\ferrosuite-AAAAMMDD.log` (se conservan 14 días). Al usuario solo se le muestra el número.

**Otros puntos abiertos**
- El `.onnx` exportado guarda en sus metadatos la ruta de la carpeta donde se entrenó (sección 14). No se reexportó, para conservar el modelo entregado.
- No hay reportes exportables (PDF o Excel).
- El pie de las pantallas enlaza a `github.com/guachulol0345896-pixel/FerroSuite-erp`; falta confirmar la URL definitiva del repositorio.
- Decisiones del equipo pendientes: la licencia del proyecto (no hay archivo LICENSE), el futuro de Gemini y la fuente de datos reales para el entrenamiento.

## 18. Estructura del repositorio

```text
FerroSuite/
├── FerroSuite.slnx                 Solución de Visual Studio 2026
├── Directory.Build.props           Configuración común (C# más reciente, nulabilidad, versión 0.5.0)
├── README.md, INFORME_FINAL_FERROSUITE.md, AUDITORIA_TECNICA_INDEPENDIENTE.md, CORRECCIONES_FINALES.md, AUTHORS.md, THIRD-PARTY-NOTICES.md
├── evidencias/                     Registros de compilación y pruebas, capturas y resultados del modelo
├── config/appsettings.json         Configuración del programa (sin credenciales)
├── src/
│   ├── FerroSuite.Dominio/         Comun, Inventario, Proveedores, Compras, Usuarios, Seguridad
│   ├── FerroSuite.Aplicacion/      Inventario, Proveedores, Compras, Usuarios, Seguridad, IA (+ IA/Pronostico), Puertos
│   ├── FerroSuite.Infraestructura/ Persistencia (SQL Server y migraciones), Seguridad (Identity), IA/Prediccion (ONNX),
│   │                               IA/Gemini, Configuracion
│   ├── FerroSuite.Presentacion.Comunicacion/  Puente.cs
│   └── FerroSuite.Presentacion.Escritorio/    Program.cs, VentanaPrincipal.cs, frontend/ (8 páginas, css/, js/)
├── database/
│   ├── migraciones/                V001, V002, V003 (se aplican solas al abrir)
│   ├── demo/datos_demo.sql         Datos SINTÉTICOS para el modo Demo
│   └── pruebas/legado_v1.sql       Base de la versión 1 para probar la migración
├── ml/                             Red neuronal: requirements.txt, scripts del flujo, paquete ferrosuite_demanda/,
│                                   datos/ (sintéticos + ficha), resultados/ (métricas, gráficas, verificaciones)
├── modelos/                        demanda_mlp.onnx, su normalización, casos de referencia y ficha
├── tests/
│   ├── FerroSuite.Pruebas/         Pruebas en memoria y suite SQL (--bd); herramienta "servidor" para el navegador
│   ├── pantallas/                  Recorridos en navegador (recorrido.js, pantalla_ia.js, revision*.js)
│   ├── revision/                   Verificaciones estáticas y ejecución completa de la revisión
│   └── resultados/                 Aquí escribe la suite --bd (vacía hasta ejecutarla en Windows)
└── docs/                           Documentación técnica, evidencias e histórico (abajo)
```

### Documentación y evolución del proyecto

| Documento | Contenido |
|---|---|
| [docs/README.md](docs/README.md) | Índice de toda la documentación |
| [docs/arquitectura.md](docs/arquitectura.md) | Capas, decisiones de la reorganización y qué se copia junto al ejecutable |
| [docs/persistencia.md](docs/persistencia.md) | Etapa 2: modelo de datos, migraciones, transacciones y concurrencia |
| [docs/seguridad.md](docs/seguridad.md) | Etapa 3: autenticación, autorización, auditoría y cuentas de demostración |
| [ml/README.md](ml/README.md) | Etapa 4: red neuronal (datos, entrenamiento, métricas, exportación) |
| [docs/pronostico.md](docs/pronostico.md) | Etapa 5: integración del modelo ONNX en C# |
| [docs/pantalla_ia.md](docs/pantalla_ia.md) | Etapa 6: pantalla de Inteligencia Artificial |
| [docs/informe_pruebas.md](docs/informe_pruebas.md) | Revisión integral: resultados, evidencias y defectos |
| [docs/Documento_Maestro_FerroSuite_final.pdf](docs/Documento_Maestro_FerroSuite_final.pdf) | Documento Maestro con la fe de erratas técnica de la versión final |
| [docs/auditoria_final.md](docs/auditoria_final.md) | Auditoría final antes de publicar: archivos, compilación, pruebas, errores conocidos y pasos para publicar |
| [docs/compilacion.md](docs/compilacion.md) | Bitácora de compilación y pruebas por etapa, y el entorno de verificación en Linux |
| [docs/auditoria/](docs/auditoria/AUDITORIA_FerroSuite.md) | Auditoría inicial del código heredado (punto de partida) |
| [docs/historico/](docs/historico/) | Documento Maestro, documento ERP original y la arquitectura de integración v1, conservados como referencia |

**Evolución:** el proyecto parte del repositorio `AR_SOFT_FerroSuite` (ver [AUTHORS.md](AUTHORS.md)) y se rehízo en etapas:
1. Auditoría y reorganización en cuatro capas.
2. Persistencia en SQL Server con migraciones.
3. Seguridad con Identity, roles y auditoría.
4. Red neuronal en PyTorch.
5. Integración ONNX en C#.
6. Pantalla de Inteligencia Artificial.
7. Revisión integral.
8. Auditoría final.
9. Cierre: corrección de D-01, D-03, D-04 y D-06, evidencias en `evidencias/` e [INFORME_FINAL_FERROSUITE.md](INFORME_FINAL_FERROSUITE.md).
10. Auditoría técnica independiente ([AUDITORIA_TECNICA_INDEPENDIENTE.md](AUDITORIA_TECNICA_INDEPENDIENTE.md)) y sus correcciones ([CORRECCIONES_FINALES.md](CORRECCIONES_FINALES.md)).

Los documentos de cada etapa describen el estado de su momento; este README describe el estado actual.
