# Arquitectura y reorganización

## Capas y proyectos

| Capa | Proyecto | Destino | Depende de | Paquetes NuGet |
|---|---|---|---|---|
| Dominio | `FerroSuite.Dominio` | net10.0 | — | — |
| Aplicación | `FerroSuite.Aplicacion` | net10.0 | Dominio | — |
| Infraestructura | `FerroSuite.Infraestructura` | net10.0 | Aplicación | Microsoft.Data.SqlClient 6.0.2, Microsoft.Extensions.Identity.Core 10.0.0, Microsoft.ML.OnnxRuntime 1.29.0 |
| Presentación | `FerroSuite.Presentacion.Comunicacion` | net10.0 | Aplicación | — |
| Presentación | `FerroSuite.Presentacion.Escritorio` (ejecutable `FerroSuite.exe`) | net10.0-windows | Comunicación, Infraestructura | Microsoft.Web.WebView2 1.0.3967.48 |
| Pruebas | `FerroSuite.Pruebas` | net10.0 | Comunicación, Infraestructura | — |

Solo el proyecto de escritorio es de Windows. Todo lo demás compila y se prueba en cualquier sistema.

## Decisiones de la reorganización

| Decisión | Por qué |
|---|---|
| **Puertos en Aplicación** (desde la etapa 2: `IRepositorioInventario`, `IRepositorioProveedores`, `IRepositorioCompras`, `IRepositorioUsuarios`, `IHasherClaves`, `IGeneradorTexto`, `IMantenimientoDatos`; en la etapa 1 era un solo `IAlmacenDatos`) | Aplicación no debe conocer SQL Server ni Gemini. Antes `AsistenteInventario` recibía `ClienteGemini` directamente y el `Puente` llamaba a `BaseDatos`. |
| **Presentación en dos proyectos** | El `Puente` no usa Windows Forms; al separarlo de la ventana se puede probar el contrato de mensajes sin Windows. |
| El `Puente` atrapa `DbException` en lugar de `SqlException` | `SqlException` hereda de `DbException`: el mensaje al usuario es el mismo y Presentación deja de depender de `Microsoft.Data.SqlClient`. |
| `config/appsettings.json` | El servidor de SQL Server y el tiempo de conexión estaban fijos en el código. El archivo no tiene lugar para claves. |
| Bases Real y Demo separadas (etapa 2; reemplaza `CargarDatosSinteticos` de la etapa 1) | La base real nunca recibe datos inventados (corrige D-01). Ver [persistencia.md](persistencia.md). |
| Gemini detrás de `IGeneradorTexto` y con `Habilitado` | Se puede apagar sin tocar código mientras el equipo decide su futuro (regla: la predicción no dependerá de Gemini). |
| Global usings por proyecto (`<Using>` en cada `.csproj`) | Cada proyecto declara en un solo lugar qué espacios de nombres de otras capas usa. |
| Pruebas sin paquetes externos | NuGet no estaba disponible en el entorno de verificación. Se pueden pasar a xUnit o MSTest más adelante sin cambiar la lógica de las pruebas. |

## Qué se copia junto al ejecutable

```text
<salida de FerroSuite.Presentacion.Escritorio>\
├── FerroSuite.exe (+ DLL de las capas y de los paquetes)
├── appsettings.json        ← config/appsettings.json
├── frontend\               ← src/FerroSuite.Presentacion.Escritorio/frontend/ (43 archivos)
├── BaseDatos\migraciones\  ← database/migraciones/V*.sql
├── BaseDatos\demo\        ← database/demo/*.sql
└── modelos\                ← modelos/*.onnx y modelos/*_normalizacion.json (demanda_mlp)
```

`VentanaPrincipal` sirve `frontend\` en la dirección interna `https://ferrosuite.example/` (solo existe dentro del programa) y
`BaseDatosSql.Preparar()` lee los scripts de `BaseDatos\` (migraciones versionadas y, en modo Demo, la demostración).

## Mapa de migración (repositorio anterior → este repositorio, etapa 1)

> Registro histórico de la etapa 1. En la etapa 2, `IAlmacenDatos`, `AlmacenSqlServer` y `BaseDatos` se reemplazaron por los
> repositorios SQL, `BaseDatosSql` y `Migrador`, y los scripts `esquema/` y `datos_sinteticos/` por `migraciones/V001` y
> `demo/` (ver [persistencia.md](persistencia.md)).

| Origen (`AR_SOFT_FerroSuite`) | Destino | Cambio |
|---|---|---|
| `FerroSuite.IA/Modelos/Entidades.cs` | `src/FerroSuite.Dominio/{Inventario,Proveedores,Compras,Comun}/*.cs` | Un archivo por entidad; mismas propiedades |
| `FerroSuite.IA/Modelos/Contratos.cs` | `src/FerroSuite.Aplicacion/IA/ContratosIA.cs` | Espacio de nombres y comentarios ("microservicio") |
| `FerroSuite.IA/Servicios/InventarioServicio.cs` | `src/FerroSuite.Aplicacion/Inventario/` | Espacio de nombres |
| `FerroSuite.IA/Servicios/ProveedorServicio.cs` | `src/FerroSuite.Aplicacion/Proveedores/` | Espacio de nombres |
| `FerroSuite.IA/Servicios/OrdenServicio.cs` | `src/FerroSuite.Aplicacion/Compras/` | Espacio de nombres |
| `FerroSuite.IA/Servicios/{CalculadoraReposicion,GeneradorRecordatorios}.cs` | `src/FerroSuite.Aplicacion/IA/` | Espacio de nombres |
| `FerroSuite.IA/Servicios/AsistenteInventario.cs` | `src/FerroSuite.Aplicacion/IA/` | Recibe `IGeneradorTexto` en lugar de `ClienteGemini` |
| `FerroSuite.IA/Datos/IAlmacenDatos.cs` | `src/FerroSuite.Aplicacion/Puertos/` | Espacio de nombres |
| — | `src/FerroSuite.Aplicacion/Puertos/{IGeneradorTexto,IMantenimientoDatos}.cs` | Nuevos puertos |
| `FerroSuite.IA/Datos/AlmacenSqlServer.cs` | `src/FerroSuite.Infraestructura/Persistencia/` | Espacio de nombres |
| `FerroSuite.IA/Datos/BaseDatos.cs` | `src/FerroSuite.Infraestructura/Persistencia/` | Servidor, tiempo de conexión y datos sintéticos configurables |
| — | `src/FerroSuite.Infraestructura/Persistencia/MantenimientoSqlServer.cs` | Implementa `IMantenimientoDatos` |
| `FerroSuite.IA/Servicios/ClienteGemini.cs` | `src/FerroSuite.Infraestructura/IA/Gemini/` | Implementa `IGeneradorTexto` |
| `FerroSuite.IA/Configuracion.cs` | `src/FerroSuite.Infraestructura/IA/Gemini/ClaveGemini.cs` | Renombrado (`Leer`, `Guardar`) |
| — | `src/FerroSuite.Infraestructura/Configuracion/OpcionesFerroSuite.cs` | Nuevo: lee `appsettings.json` |
| `FerroSuite.IA/Comunicacion/Puente.cs` | `src/FerroSuite.Presentacion.Comunicacion/` | Usa puertos; atrapa `DbException` |
| `FerroSuite.IA/{Program,VentanaPrincipal}.cs` | `src/FerroSuite.Presentacion.Escritorio/` | Espacio de nombres, composición con configuración |
| `frontend/**` | `src/FerroSuite.Presentacion.Escritorio/frontend/**` | Sin cambios (solo un comentario en `api.js`) |
| `FerroSuite.IA/BaseDatos/crear_base_datos.sql` | `database/esquema/` | Sin cambios |
| `FerroSuite.IA/BaseDatos/datos_prueba.sql` | `database/datos_sinteticos/` | Encabezado que lo identifica como datos sintéticos |
| `docs/img/*`, `diagrams/*.png` | `docs/img/`, `docs/diagramas/` | Sin cambios |
| `docs/ERP.docx`, `FerroSuite_Documento_Maestro.docx`, PDF de evidencias, `arquitectura-integracion.md` | `docs/historico/` | Sin cambios (referencia de la versión anterior) |
| `FerroSuite.web/`, `FerroSuite.IA.csproj.user`, `FerroSuite.IA.csproj`, `FerroSuite.slnx`, README anteriores, `relacion-requerimientos-prototipo.md`, `pruebas-integracion.md` | — | No migrados (ver auditoría, sección 11) |
| `.git/` | — | No copiado |
