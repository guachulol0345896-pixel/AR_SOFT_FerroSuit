// FerroSuite — Servicio de bodegas y categorías (administración). Todo pasa por el backend, que valida y audita.
window.FerroSuite = window.FerroSuite || {};

FerroSuite.catalogosService = {
  bodegas() { return FerroSuite.api.pedir("bodegas.listarTodas"); },
  crearBodega(d) { return FerroSuite.api.pedir("bodegas.crear", { nombre: d.nombre, direccion: d.direccion || null }); },
  actualizarBodega(d) {
    return FerroSuite.api.pedir("bodegas.actualizar", { bodegaId: Number(d.bodegaId), nombre: d.nombre, direccion: d.direccion || null, activa: !!d.activa });
  },
  categorias() { return FerroSuite.api.pedir("categorias.listarTodas"); },
  crearCategoria(d) { return FerroSuite.api.pedir("categorias.crear", { nombre: d.nombre }); },
  actualizarCategoria(d) {
    return FerroSuite.api.pedir("categorias.actualizar", { categoriaId: Number(d.categoriaId), nombre: d.nombre, activa: !!d.activa });
  },
};
