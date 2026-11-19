// FerroSuite — Controlador de Proveedores.
document.addEventListener("DOMContentLoaded", async () => {
  const servicio = FerroSuite.proveedoresService;
  const puede = FerroSuite.puede;
  const esc = FerroSuite.util.esc;
  const zonaResumen = document.querySelector("#resumen-proveedores");
  const zonaTabla = document.querySelector("#tabla-proveedores");

  const columnas = [
    { titulo: "Nombre", valor: (p) => esc(p.nombre) },
    { titulo: "NIT", valor: (p) => esc(p.nit) },
    { titulo: "Teléfono", valor: (p) => esc(p.telefono || "—") },
    { titulo: "Días de entrega", valor: (p) => p.diasEntrega },
    { titulo: "Cumplimiento", valor: (p) => (p.cumplimiento == null ? "—" : p.cumplimiento + " %") },
    { titulo: "Estado", valor: (p) => FerroSuite.etiqueta.html(p.activo ? "Activo" : "Inactivo", p.activo ? "ok" : "neutro") },
    {
      titulo: "Acción",
      // Un proveedor con historial no se borra: se desactiva y deja de ofrecerse en productos y órdenes.
      valor: (p) => !puede("proveedores.gestionar") ? "—" :
        `<button type="button" class="btn-chico" data-editar="${esc(p.nit)}">Editar</button> ` + (p.activo
        ? `<button type="button" class="btn-chico btn-peligro" data-desactivar="${esc(p.nit)}">Desactivar</button>`
        : `<button type="button" class="btn-chico" data-activar="${esc(p.nit)}">Activar</button>`),
    },
  ];

  let lista = [];
  let editando = null; // NIT del proveedor que se edita; null = alta

  function terminarEdicion() {
    editando = null;
    formulario.form.reset();
    formulario.form.elements.nit.readOnly = false;
    formulario.cambiarBoton("Agregar proveedor");
  }

  zonaTabla.addEventListener("click", async (e) => {
    const { desactivar, activar, editar } = e.target.dataset;
    if (editar) {
      const p = lista.find((x) => x.nit === editar);
      if (!p) return;
      editando = p.nit;
      formulario.llenar({ nombre: p.nombre, nit: p.nit, telefono: p.telefono || "", diasEntrega: p.diasEntrega });
      formulario.form.elements.nit.readOnly = true;
      formulario.cambiarBoton("Guardar cambios");
      formulario.form.scrollIntoView({ behavior: "smooth" });
      return;
    }
    if (!desactivar && !activar) return;
    await FerroSuite.util.ocupado(e.target.closest("button"), async () => {
      try {
        await (desactivar ? servicio.desactivar(desactivar) : servicio.activar(activar));
        FerroSuite.ocultarErrorPagina();
        await cargar();
      } catch (error) {
        FerroSuite.mostrarErrorPagina(error);
      }
    });
  });

  async function cargar() {
    try {
      const proveedores = (await servicio.listar()).datos;
      lista = proveedores;
      FerroSuite.ocultarErrorPagina();
      let rapido = null;
      for (const p of proveedores) if (!rapido || p.diasEntrega < rapido.diasEntrega) rapido = p;
      FerroSuite.tarjetaResumen.render(zonaResumen, [
        { titulo: "Proveedores", valor: proveedores.filter((p) => p.activo).length, detalle: "activos en la base de datos" },
        { titulo: "Entrega más rápida", valor: rapido ? rapido.diasEntrega + " días" : "—", detalle: rapido ? rapido.nombre : "", tono: "ok" },
      ]);
      FerroSuite.tabla.render(zonaTabla, columnas, proveedores, "Todavía no hay proveedores registrados.");
    } catch (error) {
      FerroSuite.mostrarErrorPagina(error);
    }
  }

  const formulario = FerroSuite.formulario.render(document.querySelector("#form-proveedor"), {
    textoBoton: "Agregar proveedor",
    campos: [
      { nombre: "nombre", etiqueta: "Nombre", requerido: true },
      { nombre: "nit", etiqueta: "NIT", placeholder: "900123456-8", requerido: true },
      { nombre: "telefono", etiqueta: "Teléfono" },
      { nombre: "diasEntrega", etiqueta: "Días de entrega", tipo: "number", requerido: true },
    ],
    validar: (d) => servicio.validar(d),
    async alEnviar(datos) {
      const r = editando ? await servicio.actualizar({ ...datos, nit: editando }) : await servicio.crear(datos);
      terminarEdicion();
      await cargar();
      return r.mensaje;
    },
  });

  await FerroSuite.sesionLista;
  if (!puede("proveedores.ver")) return FerroSuite.sinAcceso();
  cargar();
});
