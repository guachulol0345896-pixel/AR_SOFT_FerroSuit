// FerroSuite — Administración de bodegas y categorías (pantalla de Inventario, solo administrador).
// Crear, editar el nombre y activar o desactivar. El backend valida (nombres únicos, no desactivar una bodega con
// mercancía ni una categoría con productos activos) y registra cada cambio en la auditoría.
document.addEventListener("DOMContentLoaded", async () => {
  const servicio = FerroSuite.catalogosService;
  const { esc, ocupado } = FerroSuite.util;
  const avisar = () => document.dispatchEvent(new Event("ferrosuite:catalogos")); // el resto de la pantalla recarga sus listas
  const estado = (activa) => FerroSuite.etiqueta.html(activa ? "Activa" : "Inactiva", activa ? "ok" : "neutro");
  let bodegas = [];
  let categorias = [];
  let editandoBodega = null;
  let editandoCategoria = null;

  await FerroSuite.sesionLista;
  const puedeBodegas = FerroSuite.puede("bodegas.gestionar");
  const puedeCategorias = FerroSuite.puede("productos.gestionar");
  if (!puedeBodegas && !puedeCategorias) return;

  // ---------- Bodegas ----------
  const formBodega = puedeBodegas && FerroSuite.formulario.render(document.querySelector("#form-bodega"), {
    campos: [
      { nombre: "nombre", etiqueta: "Nombre de la bodega", requerido: true },
      { nombre: "direccion", etiqueta: "Dirección (opcional)" },
    ],
    textoBoton: "Agregar bodega",
    validar: (d) => (d.nombre ? [] : ["El nombre de la bodega es obligatorio."]),
    async alEnviar(d) {
      const actual = bodegas.find((b) => b.bodegaId === editandoBodega);
      const r = actual ? await servicio.actualizarBodega({ ...d, bodegaId: actual.bodegaId, activa: actual.activa }) : await servicio.crearBodega(d);
      terminarBodega();
      await cargarBodegas();
      avisar();
      return r.mensaje;
    },
  });
  function terminarBodega() {
    editandoBodega = null;
    if (formBodega) { formBodega.form.reset(); formBodega.cambiarBoton("Agregar bodega"); }
  }

  async function cargarBodegas() {
    bodegas = (await servicio.bodegas()).datos;
    FerroSuite.tabla.render(document.querySelector("#tabla-bodegas"), [
      { titulo: "Bodega", valor: (b) => `<strong>${esc(b.nombre)}</strong><span class="sub">${esc(b.direccion || "")}</span>` },
      { titulo: "Estado", valor: (b) => estado(b.activa) },
      { titulo: "Acciones", valor: (b) => `<button type="button" class="btn-chico" data-editar-bodega="${b.bodegaId}">Editar</button> ` +
          `<button type="button" class="btn-chico${b.activa ? " btn-peligro" : ""}" data-estado-bodega="${b.bodegaId}">${b.activa ? "Desactivar" : "Activar"}</button>` },
    ], bodegas, "No hay bodegas.");
  }

  // ---------- Categorías ----------
  const formCategoria = puedeCategorias && FerroSuite.formulario.render(document.querySelector("#form-categoria"), {
    campos: [{ nombre: "nombre", etiqueta: "Nombre de la categoría", requerido: true }],
    textoBoton: "Agregar categoría",
    validar: (d) => (d.nombre ? [] : ["El nombre de la categoría es obligatorio."]),
    async alEnviar(d) {
      const actual = categorias.find((c) => c.categoriaId === editandoCategoria);
      const r = actual ? await servicio.actualizarCategoria({ ...d, categoriaId: actual.categoriaId, activa: actual.activa }) : await servicio.crearCategoria(d);
      terminarCategoria();
      await cargarCategorias();
      avisar();
      return r.mensaje;
    },
  });
  function terminarCategoria() {
    editandoCategoria = null;
    if (formCategoria) { formCategoria.form.reset(); formCategoria.cambiarBoton("Agregar categoría"); }
  }

  async function cargarCategorias() {
    categorias = (await servicio.categorias()).datos;
    FerroSuite.tabla.render(document.querySelector("#tabla-categorias"), [
      { titulo: "Categoría", valor: (c) => `<strong>${esc(c.nombre)}</strong>` },
      { titulo: "Estado", valor: (c) => estado(c.activa) },
      { titulo: "Acciones", valor: (c) => `<button type="button" class="btn-chico" data-editar-categoria="${c.categoriaId}">Editar</button> ` +
          `<button type="button" class="btn-chico${c.activa ? " btn-peligro" : ""}" data-estado-categoria="${c.categoriaId}">${c.activa ? "Desactivar" : "Activar"}</button>` },
    ], categorias, "No hay categorías.");
  }

  // ---------- Acciones de las filas (un clic a la vez: el botón queda ocupado hasta la respuesta) ----------
  const mensajes = document.querySelector("#mensajes-catalogos");
  document.querySelector("#admin-catalogos").addEventListener("click", async (e) => {
    const b = e.target.closest("button");
    if (!b) return;
    const d = b.dataset;
    if (d.editarBodega) {
      const x = bodegas.find((y) => y.bodegaId === Number(d.editarBodega));
      editandoBodega = x.bodegaId;
      formBodega.llenar({ nombre: x.nombre, direccion: x.direccion || "" });
      formBodega.cambiarBoton("Guardar bodega");
      return;
    }
    if (d.editarCategoria) {
      const x = categorias.find((y) => y.categoriaId === Number(d.editarCategoria));
      editandoCategoria = x.categoriaId;
      formCategoria.llenar({ nombre: x.nombre });
      formCategoria.cambiarBoton("Guardar categoría");
      return;
    }
    if (!d.estadoBodega && !d.estadoCategoria) return;
    await ocupado(b, async () => {
      try {
        let r;
        if (d.estadoBodega) {
          const x = bodegas.find((y) => y.bodegaId === Number(d.estadoBodega));
          r = await servicio.actualizarBodega({ ...x, activa: !x.activa });
          await cargarBodegas();
        } else {
          const x = categorias.find((y) => y.categoriaId === Number(d.estadoCategoria));
          r = await servicio.actualizarCategoria({ ...x, activa: !x.activa });
          await cargarCategorias();
        }
        FerroSuite.aviso.mostrar(mensajes, [r.mensaje], "ok");
        avisar();
      } catch (error) {
        FerroSuite.aviso.mostrarError(mensajes, error);
      }
    });
  });

  try {
    await Promise.all([puedeBodegas ? cargarBodegas() : null, puedeCategorias ? cargarCategorias() : null]);
  } catch (error) {
    FerroSuite.aviso.mostrarError(mensajes, error);
  }
});
