namespace FerroSuite.Aplicacion.Usuarios;

/// <summary>
/// Usuarios, inicio de sesión y claves. Las claves solo existen como hash (PasswordHasher de ASP.NET Core Identity).
/// Los errores de ingreso son siempre el mismo mensaje, para no revelar si el usuario existe o está bloqueado.
/// </summary>
public class UsuarioServicio(IRepositorioUsuarios repositorio, IHasherClaves hasher, Autorizador autorizador, TimeProvider reloj)
{
    public const int LargoMinimoClave = 10;
    public const int LargoMaximoClave = 128;
    public const int MaximoIntentos = 5;
    public static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(15);
    public const string MensajeIngresoFallido =
        "No se pudo iniciar sesión. Revisa el usuario y la clave. Después de 5 intentos fallidos la cuenta se bloquea 15 minutos.";

    private AuditoriaServicio Auditoria => autorizador.Auditoria;
    private readonly Lazy<string> hashFicticio = new(() => hasher.Calcular(GeneradorClaves.Nueva(24)));
    private DateTime Ahora => reloj.GetLocalNow().DateTime;

    // ---------------- Ingreso ----------------

    public bool ExisteAdministrador() => repositorio.ExisteAdministradorActivo();

    public SesionUsuario IniciarSesion(string? nombreUsuario, string? clave)
    {
        nombreUsuario = Identificadores.NormalizarUsuario(nombreUsuario ?? "");
        clave ??= "";
        var intento = nombreUsuario.Length > 40 ? nombreUsuario[..40] : nombreUsuario;
        var usuario = nombreUsuario.Length is > 0 and <= 40 && clave.Length <= LargoMaximoClave
            ? repositorio.BuscarPorNombreUsuario(nombreUsuario) : null;

        if (usuario is null || !usuario.Activo || !usuario.TieneClave)
        {
            // Mismo trabajo que con un usuario real, para no revelar por el tiempo de respuesta si existe.
            hasher.Verificar(hashFicticio.Value, clave.Length <= LargoMaximoClave ? clave : "");
            Auditoria.Registrar(null, "sesion.iniciar", ResultadosAuditoria.Fallido, detalle: "Usuario inexistente, inactivo o sin clave",
                nombreUsuarioIntentado: intento);
            throw new ErrorValidacion([MensajeIngresoFallido]);
        }
        var sesionAuditoria = ParaAuditoria(usuario);
        if (usuario.BloqueadoHasta is DateTime hasta && hasta > Ahora)
        {
            hasher.Verificar(hashFicticio.Value, clave);
            Auditoria.Registrar(sesionAuditoria, "sesion.iniciar", ResultadosAuditoria.Denegado, detalle: $"Cuenta bloqueada hasta {hasta:yyyy-MM-dd HH:mm}");
            throw new ErrorValidacion([MensajeIngresoFallido]);
        }

        var resultado = hasher.Verificar(usuario.ClaveHash!, clave);
        if (resultado == ResultadoClave.Incorrecta)
        {
            var intentos = usuario.IntentosFallidos + 1;
            DateTime? bloqueo = intentos >= MaximoIntentos ? Ahora.Add(DuracionBloqueo) : null;
            repositorio.RegistrarIngresoFallido(usuario.UsuarioId, bloqueo is null ? intentos : 0, bloqueo);
            Auditoria.Registrar(sesionAuditoria, "sesion.iniciar", ResultadosAuditoria.Fallido, detalle: $"Clave incorrecta (intento {intentos} de {MaximoIntentos})");
            if (bloqueo is not null)
                Auditoria.Registrar(sesionAuditoria, "usuarios.bloqueo", ResultadosAuditoria.Exitoso, $"Usuario {usuario.NombreUsuario}",
                    $"Bloqueada {DuracionBloqueo.TotalMinutes:0} minutos por {MaximoIntentos} intentos fallidos");
            throw new ErrorValidacion([MensajeIngresoFallido]);
        }
        if (resultado == ResultadoClave.CorrectaDebeActualizarse)
        {
            repositorio.CambiarClave(usuario.UsuarioId, hasher.Calcular(clave), usuario.DebeCambiarClave);
            Auditoria.Exito(sesionAuditoria, "usuarios.actualizarHash", $"Usuario {usuario.NombreUsuario}", "Hash convertido al formato vigente");
        }
        repositorio.RegistrarIngresoExitoso(usuario.UsuarioId, Ahora);
        var sesion = new SesionUsuario(usuario.UsuarioId, usuario.NombreUsuario, usuario.NombreCompleto, usuario.RolCodigo, usuario.RolNombre)
            { DebeCambiarClave = usuario.DebeCambiarClave };
        Auditoria.Exito(sesion, "sesion.iniciar", detalle: usuario.DebeCambiarClave ? "Debe cambiar la clave" : null);
        return sesion;
    }

    /// <summary>El usuario cambia su propia clave (obligatorio si el administrador se la restableció).</summary>
    public SesionUsuario CambiarClavePropia(SesionUsuario? sesion, string? actual, string? nueva)
    {
        if (sesion is null) throw new ErrorAutorizacion("Tu sesión no está iniciada o venció. Ingresa con tu usuario y clave.", sinSesion: true);
        var usuario = repositorio.BuscarPorId(sesion.UsuarioId);
        if (usuario is not { Activo: true, TieneClave: true }) throw new ErrorAutorizacion("Tu usuario ya no está activo.", sinSesion: true);
        if ((actual ?? "").Length > LargoMaximoClave || hasher.Verificar(usuario.ClaveHash!, actual ?? "") == ResultadoClave.Incorrecta)
        {
            Auditoria.Registrar(sesion, "sesion.cambiarClave", ResultadosAuditoria.Fallido, detalle: "Clave actual incorrecta");
            throw new ErrorValidacion(["La clave actual no es correcta."]);
        }
        var errores = ValidarClave(nueva, usuario.NombreUsuario);
        if (nueva == actual) errores.Add("La clave nueva debe ser distinta de la actual.");
        if (errores.Count > 0) throw new ErrorValidacion(errores);
        repositorio.CambiarClave(usuario.UsuarioId, hasher.Calcular(nueva!), debeCambiar: false);
        Auditoria.Exito(sesion, "sesion.cambiarClave");
        return sesion with { DebeCambiarClave = false };
    }

    // ---------------- Administración de usuarios (solo con permiso GestionarUsuarios) ----------------

    public List<Rol> ListarRoles(SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarUsuarios, "roles.listar");
        return repositorio.ListarRoles();
    }

    public List<Usuario> ListarUsuarios(SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarUsuarios, "usuarios.listar");
        return repositorio.ListarUsuarios().Where(u => u.NombreUsuario != "registro.anterior").ToList();
    }

    /// <summary>Crea un usuario. Debe cambiar la clave inicial la primera vez que ingrese.</summary>
    public int Crear(SesionUsuario? sesion, string? nombreUsuario, string? nombreCompleto, string? rolCodigo, string? clave)
    {
        autorizador.Exigir(sesion, Permisos.GestionarUsuarios, "usuarios.crear");
        var id = CrearInterno(nombreUsuario, nombreCompleto, rolCodigo, clave, debeCambiar: true);
        Auditoria.Exito(sesion, "usuarios.crear", $"Usuario {Identificadores.NormalizarUsuario(nombreUsuario ?? "")}", $"Rol: {rolCodigo}");
        return id;
    }

    /// <summary>Solo la primera vez: crea el administrador si todavía no hay ninguno que pueda ingresar.</summary>
    public int CrearAdministradorInicial(string? nombreUsuario, string? nombreCompleto, string? clave)
    {
        if (ExisteAdministrador()) throw new ErrorValidacion(["Ya existe un administrador."]);
        var id = CrearInterno(nombreUsuario, nombreCompleto, Rol.Administrador, clave, debeCambiar: false);
        Auditoria.Registrar(null, "usuarios.crearAdministradorInicial", ResultadosAuditoria.Exitoso,
            $"Usuario {Identificadores.NormalizarUsuario(nombreUsuario ?? "")}", nombreUsuarioIntentado: "(primer inicio)");
        return id;
    }

    public void CambiarEstado(SesionUsuario? sesion, int usuarioId, bool activo)
    {
        var accion = activo ? "usuarios.activar" : "usuarios.desactivar";
        autorizador.Exigir(sesion, Permisos.GestionarUsuarios, accion);
        var usuario = BuscarEditable(usuarioId);
        if (!activo && usuario.UsuarioId == sesion!.UsuarioId) throw new ErrorValidacion(["No puedes desactivar tu propio usuario."]);
        repositorio.CambiarEstado(usuarioId, activo);
        if (!activo && !repositorio.ExisteAdministradorActivo())
        {
            repositorio.CambiarEstado(usuarioId, true);
            throw new ErrorValidacion(["Debe quedar al menos un administrador activo."]);
        }
        Auditoria.Exito(sesion, accion, $"Usuario {usuario.NombreUsuario}");
    }

    /// <summary>
    /// Cambia el nombre completo y el rol de un usuario. El administrador no puede cambiar su propio rol, y siempre debe
    /// quedar al menos un administrador activo. El cambio de rol rige desde el siguiente ingreso de ese usuario.
    /// </summary>
    public void Actualizar(SesionUsuario? sesion, int usuarioId, string? nombreCompleto, string? rolCodigo)
    {
        autorizador.Exigir(sesion, Permisos.GestionarUsuarios, "usuarios.actualizar");
        var usuario = BuscarEditable(usuarioId);
        nombreCompleto = (nombreCompleto ?? "").Trim();
        var errores = new List<string>();
        if (nombreCompleto.Length == 0) errores.Add("El nombre completo es obligatorio.");
        else if (nombreCompleto.Length > 100) errores.Add("El nombre completo no puede tener más de 100 caracteres.");
        else if (nombreCompleto.Any(char.IsControl)) errores.Add("El nombre completo tiene caracteres no permitidos.");
        var rol = repositorio.ListarRoles().FirstOrDefault(r => r.Codigo == rolCodigo);
        if (rol is null) errores.Add("Elige un rol válido.");
        else if (usuario.UsuarioId == sesion!.UsuarioId && rol.Codigo != usuario.RolCodigo)
            errores.Add("No puedes cambiar tu propio rol (pídeselo a otro administrador).");
        if (errores.Count > 0) throw new ErrorValidacion(errores);

        repositorio.ActualizarDatos(usuarioId, nombreCompleto, rol!.RolId);
        if (!repositorio.ExisteAdministradorActivo())
        {
            repositorio.ActualizarDatos(usuarioId, usuario.NombreCompleto, usuario.RolId);
            throw new ErrorValidacion(["Debe quedar al menos un administrador activo."]);
        }
        Auditoria.Exito(sesion, "usuarios.actualizar", $"Usuario {usuario.NombreUsuario}",
            usuario.RolCodigo != rol.Codigo ? $"Rol: {usuario.RolCodigo} → {rol.Codigo}" : "Nombre actualizado");
    }

    /// <summary>Genera una clave temporal aleatoria (se muestra una sola vez) y obliga a cambiarla al ingresar.</summary>
    public string RestablecerClave(SesionUsuario? sesion, int usuarioId)
    {
        autorizador.Exigir(sesion, Permisos.GestionarUsuarios, "usuarios.restablecerClave");
        var usuario = BuscarEditable(usuarioId);
        var temporal = GeneradorClaves.Nueva();
        repositorio.CambiarClave(usuario.UsuarioId, hasher.Calcular(temporal), debeCambiar: true);
        repositorio.Desbloquear(usuario.UsuarioId);
        Auditoria.Exito(sesion, "usuarios.restablecerClave", $"Usuario {usuario.NombreUsuario}", "Clave temporal generada (no se registra)");
        return temporal;
    }

    public void Desbloquear(SesionUsuario? sesion, int usuarioId)
    {
        autorizador.Exigir(sesion, Permisos.GestionarUsuarios, "usuarios.desbloquear");
        var usuario = BuscarEditable(usuarioId);
        repositorio.Desbloquear(usuario.UsuarioId);
        Auditoria.Exito(sesion, "usuarios.desbloquear", $"Usuario {usuario.NombreUsuario}");
    }

    /// <summary>
    /// SOLO en la base de demostración: asigna claves aleatorias a los usuarios de demostración ("demo.*") y las devuelve
    /// para mostrarlas una vez en pantalla. No se guardan en texto ni en la auditoría. En la base real se rechaza.
    /// </summary>
    public List<(string Usuario, string Clave)> GenerarClavesDemostracion(SesionUsuario? sesion, bool esBaseDemo)
    {
        autorizador.Exigir(sesion, Permisos.RestablecerDemo, "demo.generarClaves");
        if (!esBaseDemo)
        {
            Auditoria.Registrar(sesion, "demo.generarClaves", ResultadosAuditoria.Denegado, detalle: "No es la base de demostración");
            throw new ErrorValidacion(["Las cuentas de demostración solo existen en modo Demo."]);
        }
        var resultado = new List<(string, string)>();
        foreach (var u in repositorio.ListarUsuarios().Where(u => u.Activo && u.NombreUsuario.StartsWith("demo.", StringComparison.Ordinal)))
        {
            var clave = GeneradorClaves.Nueva();
            repositorio.CambiarClave(u.UsuarioId, hasher.Calcular(clave), debeCambiar: false);
            repositorio.Desbloquear(u.UsuarioId);
            resultado.Add((u.NombreUsuario, clave));
        }
        Auditoria.Exito(sesion, "demo.generarClaves", detalle: $"{resultado.Count} cuentas de demostración (claves no registradas)");
        return resultado;
    }

    // ---------------- Reglas ----------------

    /// <summary>Política de claves: 10 a 128 caracteres, con letras y números, y sin contener el nombre de usuario.</summary>
    public static List<string> ValidarClave(string? clave, string? nombreUsuario = null)
    {
        var errores = new List<string>();
        if (string.IsNullOrEmpty(clave) || clave.Length < LargoMinimoClave) errores.Add($"La clave debe tener al menos {LargoMinimoClave} caracteres.");
        else if (clave.Length > LargoMaximoClave) errores.Add($"La clave no puede tener más de {LargoMaximoClave} caracteres.");
        else if (!clave.Any(char.IsLetter) || !clave.Any(char.IsDigit)) errores.Add("La clave debe tener letras y números.");
        else if (!string.IsNullOrEmpty(nombreUsuario) && clave.Contains(nombreUsuario, StringComparison.OrdinalIgnoreCase))
            errores.Add("La clave no puede contener el nombre de usuario.");
        return errores;
    }

    private int CrearInterno(string? nombreUsuario, string? nombreCompleto, string? rolCodigo, string? clave, bool debeCambiar)
    {
        nombreUsuario = Identificadores.NormalizarUsuario(nombreUsuario ?? "");
        nombreCompleto = (nombreCompleto ?? "").Trim();
        var errores = new List<string>();
        if (Identificadores.ValidarNombreUsuario(nombreUsuario) is string e) errores.Add(e);
        else if (nombreUsuario.StartsWith("demo.", StringComparison.Ordinal) || nombreUsuario == "registro.anterior")
            errores.Add("Ese nombre de usuario está reservado.");
        else if (repositorio.BuscarPorNombreUsuario(nombreUsuario) is not null) errores.Add("Ya existe un usuario con ese nombre.");
        if (nombreCompleto.Length == 0) errores.Add("El nombre completo es obligatorio.");
        else if (nombreCompleto.Length > 100) errores.Add("El nombre completo no puede tener más de 100 caracteres.");
        else if (nombreCompleto.Any(char.IsControl)) errores.Add("El nombre completo tiene caracteres no permitidos.");
        var rol = repositorio.ListarRoles().FirstOrDefault(r => r.Codigo == rolCodigo);
        if (rol is null) errores.Add("Elige un rol válido.");
        errores.AddRange(ValidarClave(clave, nombreUsuario));
        if (errores.Count > 0) throw new ErrorValidacion(errores);

        return repositorio.Crear(new Usuario
        {
            NombreUsuario = nombreUsuario, NombreCompleto = nombreCompleto, RolId = rol!.RolId, Activo = true,
            ClaveHash = hasher.Calcular(clave!), DebeCambiarClave = debeCambiar,
        });
    }

    private Usuario BuscarEditable(int usuarioId)
    {
        var u = repositorio.BuscarPorId(usuarioId);
        if (u is null || u.NombreUsuario == "registro.anterior") throw new ErrorValidacion(["El usuario no existe."]);
        return u;
    }

    private static SesionUsuario ParaAuditoria(Usuario u) =>
        new(u.UsuarioId, u.NombreUsuario, u.NombreCompleto, u.RolCodigo, u.RolNombre) { SesionId = "" };
}
