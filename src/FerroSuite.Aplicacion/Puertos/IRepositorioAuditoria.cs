namespace FerroSuite.Aplicacion.Puertos;

/// <summary>Registro de auditoría: solo se agregan eventos; no se modifican ni se borran.</summary>
public interface IRepositorioAuditoria
{
    void Registrar(EventoAuditoria evento);
    List<EventoAuditoria> Listar(int limite, string? accion = null, int? usuarioId = null);
}
