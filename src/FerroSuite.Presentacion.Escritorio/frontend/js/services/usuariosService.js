// FerroSuite — Servicio de Usuarios y auditoría del frontend (solo administrador; el backend lo verifica).
window.FerroSuite = window.FerroSuite || {};

FerroSuite.usuariosService = {
  listar() { return FerroSuite.api.pedir("usuarios.listar"); },
  roles() { return FerroSuite.api.pedir("roles.listar"); },
  crear(d) {
    return FerroSuite.api.pedir("usuarios.crear", { nombreUsuario: d.nombreUsuario, nombreCompleto: d.nombreCompleto, rolCodigo: d.rolCodigo, clave: d.clave });
  },
  activar(usuarioId) { return FerroSuite.api.pedir("usuarios.activar", { usuarioId }); },
  desactivar(usuarioId) { return FerroSuite.api.pedir("usuarios.desactivar", { usuarioId }); },
  desbloquear(usuarioId) { return FerroSuite.api.pedir("usuarios.desbloquear", { usuarioId }); },
  restablecerClave(usuarioId) { return FerroSuite.api.pedir("usuarios.restablecerClave", { usuarioId }); },
  auditoria(limite) { return FerroSuite.api.pedir("auditoria.listar", { limite }); },
  // Editar nombre completo y rol (el backend impide cambiar el propio rol y quedarse sin administrador).
  actualizar(d) {
    return FerroSuite.api.pedir("usuarios.actualizar", { usuarioId: Number(d.usuarioId), nombreCompleto: d.nombreCompleto, rolCodigo: d.rolCodigo });
  },
};
