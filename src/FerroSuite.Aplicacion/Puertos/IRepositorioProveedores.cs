namespace FerroSuite.Aplicacion.Puertos;

/// <summary>Persistencia del módulo de Proveedores.</summary>
public interface IRepositorioProveedores
{
    List<Proveedor> Listar(bool incluirInactivos = true);
    Proveedor? Buscar(string nit);
    void Crear(Proveedor proveedor);
    void Actualizar(Proveedor proveedor);
    void CambiarEstado(string nit, bool activo);
}
