# Avisos de terceros

FerroSuite no incluye código fuente de terceros en este repositorio. Usa los siguientes paquetes NuGet, que Visual Studio
descarga al compilar y que se distribuyen junto al programa al publicarlo. Cada uno conserva su propia licencia:

| Paquete | Versión | Uso | Licencia |
|---|---|---|---|
| Microsoft.Web.WebView2 | 1.0.3967.48 | Navegador integrado de la ventana | Licencia del SDK de Microsoft Edge WebView2 (incluida en el paquete; ver https://aka.ms/webview2) |
| Microsoft.Data.SqlClient | 6.0.2 | Conexión con SQL Server | MIT (https://github.com/dotnet/SqlClient) |
| Microsoft.ML.OnnxRuntime | 1.29.0 | Ejecución local del modelo neuronal ONNX (incluye el motor nativo de ONNX Runtime) | MIT (https://github.com/microsoft/onnxruntime) |
| Microsoft.Extensions.Identity.Core | 10.0.0 | `PasswordHasher` de ASP.NET Core Identity para el hash de claves (trae sus dependencias Microsoft.Extensions.Options, Logging.Abstractions y Cryptography.KeyDerivation) | MIT (https://github.com/dotnet/aspnetcore) |

Requisitos externos que no se redistribuyen desde este repositorio: Microsoft Edge WebView2 Runtime, SQL Server Express LocalDB y .NET 10.

## Herramientas de desarrollo (no se incluyen ni se redistribuyen)

Se instalan aparte para reentrenar la red o ejecutar pruebas; el programa no las necesita. Licencias según los metadatos
de cada paquete en la versión usada:

| Herramienta | Versión | Uso | Licencia |
|---|---|---|---|
| PyTorch (`torch`) | 2.15.0 | Entrenamiento y exportación a ONNX (`ml/`) | BSD-3-Clause y licencias de sus componentes (declaradas en el paquete) |
| NumPy | 2.5.3 | Datos y métricas (`ml/`) | BSD-3-Clause (y licencias de componentes incluidos) |
| pandas | 3.0.5 | Datos (`ml/`) | BSD-3-Clause |
| Matplotlib | 3.11.2 | Gráficas de resultados (`ml/`) | Licencia de Matplotlib (basada en la PSF) |
| ONNX | 1.23.2 | Verificación del modelo (`ml/`) | Apache-2.0 |
| ONNX Script | 0.7.2 | Requerido por el exportador `dynamo` de PyTorch (`ml/`) | MIT |
| ONNX Runtime (Python) | 1.29.0 | Comparar ONNX con PyTorch (`ml/`) | MIT |
| Playwright | 1.63.0 | Recorridos de pantallas en navegador (`tests/pantallas/`) | Apache-2.0 |

El modelo `modelos/demanda_mlp.onnx` y los datos sintéticos de `ml/datos/` los produjo el equipo con estas herramientas;
no contienen código ni datos de terceros.

El proyecto anterior incluía, dentro de `FerroSuite.web/wwwroot/lib`, copias de Bootstrap, jQuery, jQuery Validation y
jQuery Validation Unobtrusive con sus avisos de licencia (MIT). Esa carpeta no se migró porque no la usaba el programa;
por eso ya no se redistribuyen ni se necesitan sus avisos.

Pendiente del equipo: elegir la licencia propia del proyecto (el repositorio original no tenía archivo LICENSE).
