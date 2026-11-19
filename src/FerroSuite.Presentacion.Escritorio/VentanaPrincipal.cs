using System.Diagnostics;
using System.Net;
using FerroSuite.Infraestructura.Configuracion;
using FerroSuite.Infraestructura.IA.Gemini;
using FerroSuite.Infraestructura.IA.Prediccion;
using FerroSuite.Infraestructura.Persistencia;
using FerroSuite.Infraestructura.Seguridad;
using FerroSuite.Presentacion.Comunicacion;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FerroSuite.Presentacion.Escritorio;

/// <summary>
/// Ventana del aplicativo. Muestra las pantallas (carpeta frontend) dentro de un navegador integrado (WebView2)
/// y pasa sus mensajes al backend a través del Puente. No abre ningún puerto ni usa localhost.
/// WebView2 solo navega por las pantallas propias; los mensajes de cualquier otra página se rechazan.
/// </summary>
public class VentanaPrincipal : Form
{
    private const string Dominio = "ferrosuite.example"; // dirección interna, solo existe dentro del programa

    private readonly WebView2 vista = new() { Dock = DockStyle.Fill };
    private readonly OpcionesFerroSuite opciones;
    private readonly ClienteGemini gemini;
    private readonly BaseDatosSql baseDatos;
    private readonly UsuarioServicio usuarios;
    private readonly AdministracionServicio administracion;
    private readonly GestorSesion sesiones;
    private readonly PronosticadorOnnx pronosticador;
    private readonly Puente puente;
    private readonly List<ToolStripMenuItem> soloAdministrador = [];

    public VentanaPrincipal()
    {
        Text = "FerroSuite";
        Width = 1280;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;

        // Configuración (appsettings.json junto al ejecutable, sin credenciales).
        opciones = OpcionesFerroSuite.Cargar(aviso: m => Trace.WriteLine(m));
        baseDatos = new BaseDatosSql(opciones.BaseDatos);
        if (baseDatos.EsDemo) Text = "FerroSuite — DEMOSTRACIÓN (datos sintéticos)";

        // Raíz de composición: Infraestructura → Aplicación → Presentación.
        var reloj = TimeProvider.System;
        var repoInventario = new RepositorioInventarioSql(baseDatos);
        var repoProveedores = new RepositorioProveedoresSql(baseDatos);
        var repoCompras = new RepositorioComprasSql(baseDatos);
        var autorizador = new Autorizador(new AuditoriaServicio(new RepositorioAuditoriaSql(baseDatos), reloj));
        sesiones = new GestorSesion(reloj);
        usuarios = new UsuarioServicio(new RepositorioUsuariosSql(baseDatos), new HasherIdentity(), autorizador, reloj);
        administracion = new AdministracionServicio(autorizador, new MantenimientoSqlServer(baseDatos));
        var calculadora = new CalculadoraReposicion();
        // Modelo neuronal local (ONNX Runtime). Si no carga, queda "no disponible" y se usa la regla de stock mínimo.
        pronosticador = new PronosticadorOnnx();
        Trace.WriteLine("Modelo de demanda: " + (pronosticador.Estado.Disponible ? pronosticador.Estado.Descripcion : pronosticador.Estado.Motivo));
        gemini = new ClienteGemini(new HttpClient(), new OpcionesGemini
        {
            ApiKey = opciones.Gemini.Habilitado ? ClaveGemini.Leer() : "",
            Modelo = opciones.Gemini.Modelo,
            TimeoutSegundos = opciones.Gemini.TiempoEsperaSegundos,
        });
        puente = new Puente(new ServiciosFerroSuite(
            new InventarioServicio(repoInventario, repoProveedores, repoCompras, calculadora, autorizador),
            new ProveedorServicio(repoProveedores, autorizador),
            new OrdenServicio(repoCompras, repoInventario, repoProveedores, reloj, autorizador),
            usuarios, administracion, new AsistenteInventario(calculadora, gemini, reloj),
            new PronosticoServicio(repoInventario, repoCompras, pronosticador, autorizador, reloj), gemini, autorizador, sesiones));
        FormClosed += (s, e) => pronosticador.Dispose();

        Controls.Add(vista);
        Controls.Add(CrearMenu());
        // Los menús de administración se habilitan solo con sesión de administrador (comodidad: cada acción vuelve a verificarlo).
        sesiones.Cambio += s => { if (IsHandleCreated) BeginInvoke(() => ActualizarMenu(s)); };
        Load += async (s, e) => await IniciarAsync();
    }

    private MenuStrip CrearMenu()
    {
        var menu = new MenuStrip();
        var menuOpciones = new ToolStripMenuItem("Opciones");
        menuOpciones.DropDownItems.Add("Recargar pantalla", null, (s, e) => vista.Reload());
        menuOpciones.DropDownItems.Add(new ToolStripSeparator());
        menuOpciones.DropDownItems.Add(Admin("Clave de Gemini…", (s, e) => PedirClaveGemini()));
        menuOpciones.DropDownItems.Add(Admin("Probar conexión con Gemini", async (s, e) => await ProbarGeminiAsync()));

        // Opciones para la prueba de integración 4 (error de comunicación o de conexión). Solo administrador.
        var sinComunicacion = Admin("Simular error de comunicación", null);
        sinComunicacion.Click += (s, e) => Configurar("configuracion.simularComunicacion", !sinComunicacion.Checked,
            v => { puente.SimularDesconexion = v; sinComunicacion.Checked = v; }, "Simular error de comunicación");
        var sinBaseDatos = Admin("Simular caída de la base de datos", null);
        sinBaseDatos.Click += (s, e) => Configurar("configuracion.simularCaidaBD", !sinBaseDatos.Checked,
            v => { baseDatos.SimularCaida = v; sinBaseDatos.Checked = v; }, "Simular caída de la base de datos");
        menuOpciones.DropDownItems.Add(new ToolStripSeparator());
        menuOpciones.DropDownItems.Add(sinComunicacion);
        menuOpciones.DropDownItems.Add(sinBaseDatos);

        if (baseDatos.EsDemo)
        {
            menuOpciones.DropDownItems.Add(new ToolStripSeparator());
            menuOpciones.DropDownItems.Add(Admin("Generar claves de las cuentas de demostración…", (s, e) => GenerarClavesDemo()));
            menuOpciones.DropDownItems.Add(Admin("Reiniciar base de demostración…", async (s, e) => await ReiniciarDemoAsync()));
        }

        menu.Items.Add(menuOpciones);
        MainMenuStrip = menu;
        ActualizarMenu(null);
        return menu;
    }

    private ToolStripMenuItem Admin(string texto, EventHandler? alHacerClic)
    {
        var item = new ToolStripMenuItem(texto, null, alHacerClic) { ToolTipText = "Requiere sesión de administrador" };
        soloAdministrador.Add(item);
        return item;
    }

    private void ActualizarMenu(SesionUsuario? sesion)
    {
        var admin = Permisos.Tiene(sesion, Permisos.ModificarConfiguracion) && sesion is { DebeCambiarClave: false };
        foreach (var item in soloAdministrador) item.Enabled = admin;
    }

    // Todo cambio de configuración pasa por AdministracionServicio: exige permiso y queda en la auditoría.
    private bool Ejecutar(Action accion)
    {
        try
        {
            accion();
            return true;
        }
        catch (ErrorAutorizacion ex) { Aviso(ex.Message, MessageBoxIcon.Warning); }
        catch (ErrorValidacion ex) { Aviso(string.Join("\n", ex.Errores), MessageBoxIcon.Warning); }
        catch (Exception ex)
        {
            var referencia = RegistroTecnico.Error("opciones", ex);
            Aviso($"No se pudo completar la operación. Referencia: {referencia}.", MessageBoxIcon.Error);
        }
        return false;
    }

    private void Configurar(string accion, bool valor, Action<bool> aplicar, string descripcion) =>
        Ejecutar(() => administracion.CambiarConfiguracion(puente.Sesion, accion, $"{descripcion}: {(valor ? "activado" : "desactivado")}", () => aplicar(valor)));

    private async Task IniciarAsync()
    {
        try
        {
            // Los datos de WebView2 (caché, etc.) van en la carpeta del usuario, no junto al ejecutable: así el programa
            // también arranca si se instala en una carpeta sin permiso de escritura (por ejemplo, Program Files).
            var carpetaDatos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerroSuite", "WebView2");
            Directory.CreateDirectory(carpetaDatos);
            var entorno = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: carpetaDatos);
            await vista.EnsureCoreWebView2Async(entorno);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            RegistroTecnico.Error("webview2.runtime", ex);
            MessageBox.Show("Falta Microsoft Edge WebView2 Runtime. Instálalo desde microsoft.com y vuelve a abrir FerroSuite.",
                "FerroSuite", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        catch (Exception ex)
        {
            var referencia = RegistroTecnico.Error("webview2.inicio", ex);
            MessageBox.Show($"No se pudo iniciar el visor de pantallas (WebView2). Reinstala o repara Microsoft Edge WebView2 Runtime " +
                $"y vuelve a abrir FerroSuite.\n\nReferencia: {referencia} (detalle en {RegistroTecnico.Carpeta})",
                "FerroSuite", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        Endurecer(vista.CoreWebView2);

        vista.CoreWebView2.NavigateToString(Pagina("Preparando la base de datos…", "La primera vez puede tardar unos segundos."));

        // La base de datos se prepara en segundo plano para no congelar la ventana.
        try
        {
            await Task.Run(baseDatos.Preparar);
        }
        catch (Exception ex)
        {
            var referencia = RegistroTecnico.Error("basedatos.preparar", ex);
            vista.CoreWebView2.NavigateToString(Pagina("No se pudo conectar con la base de datos",
                "Revisa que SQL Server Express LocalDB esté instalado (Visual Studio Installer → Componentes individuales → " +
                $"SQL Server Express LocalDB) y que la instancia esté iniciada (sqllocaldb start MSSQLLocalDB). Luego vuelve a abrir el programa. " +
                $"Referencia: {referencia}. El detalle técnico quedó en {RegistroTecnico.Carpeta}."));
            return;
        }

        // Primera vez (o sin ningún administrador que pueda ingresar): se crea uno. No hay claves por defecto.
        try
        {
            if (!await Task.Run(usuarios.ExisteAdministrador) && !CrearAdministradorInicial())
            {
                vista.CoreWebView2.NavigateToString(Pagina("Falta crear el administrador",
                    "FerroSuite necesita un usuario administrador para empezar. Cierra y vuelve a abrir el programa para crearlo."));
                return;
            }
        }
        catch (Exception ex)
        {
            var referencia = RegistroTecnico.Error("usuarios.administrador", ex);
            vista.CoreWebView2.NavigateToString(Pagina("No se pudo leer la lista de usuarios",
                $"Vuelve a abrir el programa. Si se repite, revisa la base de datos. Referencia: {referencia}."));
            return;
        }

        var carpetaFrontend = Path.Combine(AppContext.BaseDirectory, "frontend");
        vista.CoreWebView2.SetVirtualHostNameToFolderMapping(Dominio, carpetaFrontend, CoreWebView2HostResourceAccessKind.Allow);
        vista.CoreWebView2.WebMessageReceived += AlRecibirMensaje;
        vista.CoreWebView2.Navigate($"https://{Dominio}/login.html");
    }

    /// <summary>Restringe WebView2: solo las pantallas propias, sin ventanas nuevas y sin herramientas de desarrollo en la versión final.</summary>
    private void Endurecer(CoreWebView2 web)
    {
        var ajustes = web.Settings;
#if DEBUG
        ajustes.AreDevToolsEnabled = true;
#else
        ajustes.AreDevToolsEnabled = false;
        ajustes.AreDefaultContextMenusEnabled = false;
        ajustes.AreBrowserAcceleratorKeysEnabled = false;
#endif
        ajustes.IsStatusBarEnabled = false;
        ajustes.IsGeneralAutofillEnabled = false;
        ajustes.IsPasswordAutosaveEnabled = false;

        web.NavigationStarting += (s, e) =>
        {
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && (uri.Host == Dominio || uri.Scheme == "data" || uri.AbsoluteUri == "about:blank"))
                return;
            e.Cancel = true;
            AbrirFuera(e.Uri);
        };
        web.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            AbrirFuera(e.Uri);
        };
    }

    // Los enlaces externos (por ejemplo, el repositorio) se abren en el navegador del sistema, nunca dentro del programa.
    private static void AbrirFuera(string direccion)
    {
        if (Uri.TryCreate(direccion, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    // Llega una solicitud del frontend: el Puente la atiende (comprobando origen, sesión y permisos) y se devuelve la respuesta.
    private async void AlRecibirMensaje(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var respuesta = await puente.AtenderAsync(e.WebMessageAsJson, e.Source);
            if (respuesta is null || vista.CoreWebView2 is null) return;
            vista.CoreWebView2.PostWebMessageAsJson(respuesta);
        }
        catch (Exception ex)
        {
            // Por ejemplo, la ventana se cerró mientras se respondía. La pantalla recibe su aviso por tiempo de espera.
            RegistroTecnico.Error("puente.respuesta", ex);
        }
    }

    /// <summary>Ventana para crear el primer administrador. Devuelve false si el usuario cancela.</summary>
    private bool CrearAdministradorInicial()
    {
        while (true)
        {
            using var ventana = new Form
            {
                Text = "FerroSuite — Crear administrador",
                Width = 460,
                Height = 330,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
            };
            var intro = new Label
            {
                Left = 16, Top = 12, Width = 410, Height = 40,
                Text = "Es la primera vez que se usa esta base de datos. Crea el usuario del dueño o administrador.",
            };
            Label Etiqueta(string texto, int top) => new() { Text = texto, Left = 16, Top = top + 3, Width = 140 };
            var usuario = new TextBox { Left = 160, Top = 60, Width = 260, MaxLength = 40 }; // minúsculas, números, punto, guion: ej. maria.gomez
            var nombre = new TextBox { Left = 160, Top = 95, Width = 260, MaxLength = 100 };
            var clave = new TextBox { Left = 160, Top = 130, Width = 260, UseSystemPasswordChar = true, MaxLength = UsuarioServicio.LargoMaximoClave };
            var repetir = new TextBox { Left = 160, Top = 165, Width = 260, UseSystemPasswordChar = true, MaxLength = UsuarioServicio.LargoMaximoClave };
            var ayuda = new Label { Left = 16, Top = 200, Width = 410, Height = 32, Text = $"La clave debe tener al menos {UsuarioServicio.LargoMinimoClave} caracteres, con letras y números, y no contener el usuario." };
            var crear = new Button { Text = "Crear", Left = 240, Top = 240, Width = 85, DialogResult = DialogResult.OK };
            var cancelar = new Button { Text = "Cancelar", Left = 335, Top = 240, Width = 85, DialogResult = DialogResult.Cancel };
            ventana.Controls.AddRange([intro, Etiqueta("Usuario", 60), usuario, Etiqueta("Nombre completo", 95), nombre,
                Etiqueta("Clave", 130), clave, Etiqueta("Repite la clave", 165), repetir, ayuda, crear, cancelar]);
            ventana.AcceptButton = crear;
            ventana.CancelButton = cancelar;

            if (ventana.ShowDialog(this) != DialogResult.OK) return false;
            if (clave.Text != repetir.Text)
            {
                Aviso("Las dos claves no coinciden.", MessageBoxIcon.Warning);
                continue;
            }
            try
            {
                usuarios.CrearAdministradorInicial(usuario.Text, nombre.Text, clave.Text);
                Aviso("Administrador creado. Ingresa con ese usuario y clave.", MessageBoxIcon.Information);
                return true;
            }
            catch (ErrorValidacion ex)
            {
                Aviso(string.Join("\n", ex.Errores), MessageBoxIcon.Warning);
            }
        }
    }

    // Solo en modo demostración: claves aleatorias para las cuentas demo.*, mostradas una sola vez (no se guardan en texto).
    private void GenerarClavesDemo()
    {
        if (MessageBox.Show("Se asignarán claves nuevas y aleatorias a las cuentas de demostración (demo.bodega, demo.vendedor, demo.contador).\n" +
                "Se muestran una sola vez. ¿Continuar?", "FerroSuite", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        List<(string Usuario, string Clave)> cuentas = [];
        if (!Ejecutar(() => cuentas = usuarios.GenerarClavesDemostracion(puente.Sesion, baseDatos.EsDemo))) return;

        using var ventana = new Form
        {
            Text = "Cuentas de demostración (datos sintéticos)", Width = 480, Height = 300,
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
        };
        var texto = new TextBox
        {
            Left = 16, Top = 16, Width = 430, Height = 180, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = "Solo para la base de DEMOSTRACIÓN. Copia estas claves ahora: no se vuelven a mostrar.\r\n\r\n"
                   + string.Join("\r\n", cuentas.Select(c => $"{c.Usuario}    {c.Clave}")),
        };
        var cerrar = new Button { Text = "Cerrar", Left = 361, Top = 210, Width = 85, DialogResult = DialogResult.OK };
        ventana.Controls.AddRange([texto, cerrar]);
        ventana.AcceptButton = cerrar;
        ventana.ShowDialog(this);
    }

    // Solo en modo demostración: borra y vuelve a crear la base de DEMOSTRACIÓN. La base real no se toca.
    private async Task ReiniciarDemoAsync()
    {
        if (MessageBox.Show($"Se borrará la base de demostración ({baseDatos.NombreBase}) y se volverá a crear con los datos sintéticos.\n\n¿Continuar?",
                "FerroSuite", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var sesion = puente.Sesion;
        try
        {
            Cursor = Cursors.WaitCursor;
            await Task.Run(() => administracion.RestablecerDemo(sesion));
            puente.CerrarSesion(); // los usuarios de la base anterior ya no existen
            Cursor = Cursors.Default;
            if (!usuarios.ExisteAdministrador()) CrearAdministradorInicial();
            vista.CoreWebView2?.Navigate($"https://{Dominio}/login.html");
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            Aviso(ex is ErrorAutorizacion or ErrorValidacion ? ex.Message : "No se pudo reiniciar la demostración (" + ex.GetType().Name + ").", MessageBoxIcon.Warning);
        }
    }

    private void PedirClaveGemini()
    {
        if (!Permisos.Tiene(puente.Sesion, Permisos.ModificarConfiguracion))
        {
            Aviso("Solo el administrador puede cambiar la configuración.", MessageBoxIcon.Warning);
            return;
        }
        using var ventana = new Form
        {
            Text = "Clave de Gemini",
            Width = 520,
            Height = 210,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
        };
        var texto = new Label
        {
            Text = "Pega la clave que sacaste en aistudio.google.com/apikey (déjala vacía para borrarla). Se guarda cifrada con " +
                   "Windows (DPAPI). Aviso: con la clave, el asistente envía a Google nombres de productos y proveedores, " +
                   "cantidades y costos estimados. El pronóstico neuronal no usa Gemini.",
            Left = 16, Top = 10, Width = 470, Height = 64,
        };
        // La clave guardada no se vuelve a mostrar: el campo empieza vacío.
        var campo = new TextBox { Left = 16, Top = 80, Width = 470, UseSystemPasswordChar = true, MaxLength = 200 };
        var guardar = new Button { Text = "Guardar", Left = 300, Top = 116, Width = 90, DialogResult = DialogResult.OK };
        var cancelar = new Button { Text = "Cancelar", Left = 396, Top = 116, Width = 90, DialogResult = DialogResult.Cancel };
        ventana.Controls.AddRange([texto, campo, guardar, cancelar]);
        ventana.AcceptButton = guardar;
        ventana.CancelButton = cancelar;

        if (ventana.ShowDialog(this) != DialogResult.OK) return;

        var nueva = campo.Text.Trim();
        string? error = null;
        // La descripción de la auditoría nunca lleva la clave.
        var ok = Ejecutar(() => administracion.CambiarConfiguracion(puente.Sesion, "configuracion.claveGemini",
            nueva.Length == 0 ? "Clave de Gemini borrada" : "Clave de Gemini reemplazada", () =>
            {
                if (opciones.Gemini.Habilitado) gemini.CambiarClave(nueva);
                error = ClaveGemini.Guardar(nueva);
            }));
        if (!ok) return;
        if (error is not null)
            Aviso(error + "\n\nPor ahora la clave sí se usa, pero tendrás que ponerla otra vez al abrir el programa.", MessageBoxIcon.Warning);
        else
            Aviso(gemini.Configurado ? "Clave guardada. El asistente ya usa Gemini." : "Clave borrada. El asistente responderá con reglas.", MessageBoxIcon.Information);
        vista.Reload();
    }

    // Hace una pregunta de prueba a Gemini y muestra el resultado o el error exacto.
    private async Task ProbarGeminiAsync()
    {
        if (!Permisos.Tiene(puente.Sesion, Permisos.ModificarConfiguracion))
        {
            Aviso("Solo el administrador puede probar la configuración.", MessageBoxIcon.Warning);
            return;
        }
        Cursor = Cursors.WaitCursor;
        var resultado = await gemini.ProbarAsync();
        Cursor = Cursors.Default;
        MessageBox.Show(resultado, "Prueba de Gemini", MessageBoxButtons.OK,
            gemini.UltimoError is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void Aviso(string texto, MessageBoxIcon icono) => MessageBox.Show(this, texto, "FerroSuite", MessageBoxButtons.OK, icono);

    private static string Pagina(string titulo, string detalle) => $"""
        <html><body style="font-family:Segoe UI,Arial;background:#efede8;color:#202d3f;padding:48px">
        <h2>{WebUtility.HtmlEncode(titulo)}</h2><p>{WebUtility.HtmlEncode(detalle)}</p></body></html>
        """;
}
