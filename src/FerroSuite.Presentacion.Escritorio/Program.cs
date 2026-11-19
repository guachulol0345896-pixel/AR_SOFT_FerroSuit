// FerroSuite — Aplicativo de escritorio.
// Presentación: este proyecto (ventana + pantallas de la carpeta "frontend") y FerroSuite.Presentacion.Comunicacion (Puente).
// Aplicación: FerroSuite.Aplicacion (casos de uso y validaciones).
// Dominio: FerroSuite.Dominio (entidades).
// Infraestructura: FerroSuite.Infraestructura (SQL Server LocalDB, configuración, Gemini opcional).
namespace FerroSuite.Presentacion.Escritorio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        RegistroTecnico.Iniciar();
        // Cualquier error no previsto se registra con un número de referencia y se avisa sin detalles técnicos.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AvisarError("ventana", e.Exception, puedeSeguir: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AvisarError("proceso", ex, puedeSeguir: false);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            RegistroTecnico.Error("tarea", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new VentanaPrincipal());
    }

    private static void AvisarError(string donde, Exception ex, bool puedeSeguir)
    {
        var referencia = RegistroTecnico.Error(donde, ex);
        MessageBox.Show(
            $"Ocurrió un error inesperado (referencia {referencia}).\n\n" +
            (puedeSeguir ? "Puedes seguir trabajando. " : "El programa se cerrará. ") +
            $"El detalle quedó en el registro técnico:\n{RegistroTecnico.Carpeta}",
            "FerroSuite", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
