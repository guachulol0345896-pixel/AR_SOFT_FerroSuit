namespace FerroSuite.Aplicacion.Proveedores;

/// <summary>Lógica de negocio del módulo de Proveedores.</summary>
public class ProveedorServicio(IRepositorioProveedores repositorio, Autorizador autorizador)
{
    private AuditoriaServicio Auditoria => autorizador.Auditoria;

    public List<Proveedor> Listar() => repositorio.Listar(incluirInactivos: true);

    public void Crear(Proveedor d, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarProveedores, "proveedores.crear");
        Limpiar(d);
        var errores = Validar(d);
        if (Identificadores.ValidarNit(d.Nit) is string errorNit) errores.Insert(0, errorNit);
        else if (repositorio.Buscar(d.Nit) is not null || repositorio.Buscar(NitSinDigito(d.Nit)) is not null
                 || repositorio.Listar().Any(p => NitSinDigito(p.Nit) == NitSinDigito(d.Nit)))
            errores.Insert(0, "Ya existe un proveedor con ese NIT.");
        if (errores.Count > 0) throw new ErrorValidacion(errores);
        d.Activo = true;
        d.Cumplimiento = null;
        repositorio.Crear(d);
        Auditoria.Exito(sesion, "proveedores.crear", $"Proveedor {d.Nit}");
    }

    /// <summary>Cambia nombre, teléfono y días de entrega. El NIT no se cambia (identifica al proveedor).</summary>
    public void Actualizar(Proveedor d, SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.GestionarProveedores, "proveedores.actualizar");
        Limpiar(d);
        var actual = repositorio.Buscar(d.Nit) ?? throw new ErrorValidacion(["El proveedor no existe."]);
        var errores = Validar(d);
        if (errores.Count > 0) throw new ErrorValidacion(errores);
        d.Activo = actual.Activo;
        d.Cumplimiento = actual.Cumplimiento;
        repositorio.Actualizar(d);
        Auditoria.Exito(sesion, "proveedores.actualizar", $"Proveedor {d.Nit}");
    }

    public void CambiarEstado(string nit, bool activo, SesionUsuario? sesion)
    {
        var accion = activo ? "proveedores.activar" : "proveedores.desactivar";
        autorizador.Exigir(sesion, Permisos.GestionarProveedores, accion);
        nit = Identificadores.NormalizarNit(nit ?? "");
        if (nit.Length is 0 or > 20 || repositorio.Buscar(nit) is null) throw new ErrorValidacion(["El proveedor no existe."]);
        repositorio.CambiarEstado(nit, activo);
        Auditoria.Exito(sesion, accion, $"Proveedor {nit}");
    }

    private static void Limpiar(Proveedor d)
    {
        d.Nit = Identificadores.NormalizarNit(d.Nit);
        d.Nombre = (d.Nombre ?? "").Trim();
        d.Telefono = string.IsNullOrWhiteSpace(d.Telefono) ? null : d.Telefono.Trim();
    }

    private static List<string> Validar(Proveedor d)
    {
        var errores = new List<string>();
        if (d.Nombre.Length == 0) errores.Add("El nombre del proveedor es obligatorio.");
        else if (d.Nombre.Length > 120) errores.Add("El nombre no puede tener más de 120 caracteres.");
        if (d.Telefono?.Length > 30) errores.Add("El teléfono no puede tener más de 30 caracteres.");
        if (d.DiasEntrega < 0 || d.DiasEntrega > 90) errores.Add("Los días de entrega deben estar entre 0 y 90.");
        return errores;
    }

    // "900123456-8" y "900123456" son el mismo proveedor.
    private static string NitSinDigito(string nit) => nit.Split('-')[0];
}
