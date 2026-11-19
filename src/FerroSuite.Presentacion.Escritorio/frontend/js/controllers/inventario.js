// FerroSuite — Controlador de Inventario.
// Pide los datos al backend, los muestra y envía lo que el usuario registra.
// Solo pide y muestra lo que el rol permite (el backend rechaza lo demás de todas formas).
document.addEventListener("DOMContentLoaded", async () => {
  const servicio = FerroSuite.inventarioService;
  const puede = FerroSuite.puede;
  const { esc, numero, cantidad, pesos, fecha } = FerroSuite.util;

  const buscador = document.querySelector("#buscar-producto");
  const filtroCategoria = document.querySelector("#filtro-categoria");
  const filtroRiesgo = document.querySelector("#filtro-riesgo");
  const tituloProducto = document.querySelector("#titulo-form-producto");
  const botonCancelar = document.querySelector("#btn-cancelar-edicion");
  const mensajesCatalogo = document.querySelector("#mensajes-catalogo");

  let productos = []; // última lista recibida del backend
  let catalogosCargados = false;

  const lista = (valores) => valores.map((v) => ({ valor: v, texto: v }));

  const columnasProductos = [
    { titulo: "Producto", valor: (p) => `<strong>${esc(p.nombre)}</strong><span class="sub">${esc(p.sku)}</span>` },
    { titulo: "Categoría", valor: (p) => esc(p.categoria) },
    { titulo: "Stock", valor: (p) => cantidad(p.stockActual, p.unidad) + `<span class="sub">mínimo ${numero(p.stockMinimo)}</span>` },
    { titulo: "Se acaba en", valor: (p) => (p.diasRestantes == null ? "Sin ventas" : p.diasRestantes + " días") },
    { titulo: "Precio compra", valor: (p) => pesos(p.precioCompra) },
    { titulo: "Estado", valor: (p) => FerroSuite.etiqueta.html(p.estado, p.estado === "Normal" ? "neutro" : "alerta") },
    {
      titulo: "Acciones",
      valor: (p) => [
        puede("productos.gestionar") ? `<button type="button" class="btn-chico" data-editar="${esc(p.sku)}">Editar</button>` : "",
        puede("productos.eliminar") ? `<button type="button" class="btn-chico btn-peligro" data-eliminar="${esc(p.sku)}">Eliminar</button>` : "",
      ].join(" ") || "—",
    },
  ];

  const columnasMovimientos = [
    { titulo: "Fecha", valor: (m) => fecha(m.fecha) },
    { titulo: "Producto", valor: (m) => esc(m.nombreProducto) },
    { titulo: "Tipo", valor: (m) => FerroSuite.etiqueta.html(m.tipo, m.tipo === "Entrada" ? "ok" : "neutro") },
    { titulo: "Cantidad", valor: (m) => cantidad(m.cantidad, m.unidad) },
    { titulo: "Motivo", valor: (m) => esc(m.motivo) + (m.referencia ? `<span class="sub">${esc(m.referencia)}</span>` : "") },
    { titulo: "Bodega", valor: (m) => esc(m.nombreBodega) },
    { titulo: "Responsable", valor: (m) => esc(m.usuario) },
  ];

  // Filtra en pantalla la lista que ya llegó del backend.
  function pintarCatalogo() {
    const texto = buscador.value.trim().toLowerCase();
    const filas = productos.filter((p) =>
      (!filtroCategoria.value || p.categoria === filtroCategoria.value) &&
      (!filtroRiesgo.checked || p.estado !== "Normal") &&
      (p.sku.toLowerCase().includes(texto) || p.nombre.toLowerCase().includes(texto))
    );
    FerroSuite.tabla.render(document.querySelector("#tabla-productos"), columnasProductos, filas, "No hay productos que coincidan.");
  }

  // Pide todo al backend y vuelve a dibujar la pantalla.
  // Categorías y bodegas vienen de la base de datos.
  async function cargarCatalogos() {
    const [rCategorias, rBodegas, rEstado] = await Promise.all([
      servicio.listarCategorias(), servicio.listarBodegas(), servicio.estadoSistema(),
    ]);
    const categorias = rCategorias.datos.map((c) => ({ valor: c.categoriaId, texto: c.nombre }));
    const bodegas = rBodegas.datos.map((b) => ({ valor: b.bodegaId, texto: b.nombre }));
    filtroCategoria.innerHTML = '<option value="">Todas las categorías</option>' +
      rCategorias.datos.map((c) => `<option>${esc(c.nombre)}</option>`).join("");
    formProducto.actualizarOpciones("categoriaId", categorias);
    formProducto.actualizarOpciones("bodegaId", bodegas);
    formMovimiento.actualizarOpciones("bodegaId", bodegas);
    // Reconstruir la base solo existe en modo demostración: la base real nunca se borra.
    document.querySelector("#btn-restablecer").hidden = !(rEstado.datos.modoDemo && puede("demo.restablecer"));
    // Cada rol ve solo los motivos que puede registrar: ventas y devoluciones (mostrador) o ajustes (bodega).
    const motivos = MOTIVOS.filter((m) => puede(["Venta", "Devolución"].includes(m) ? "movimientos.venta" : "movimientos.ajuste"));
    formMovimiento.actualizarOpciones("motivo", lista(motivos));
    catalogosCargados = true;
  }

  // Si el administrador cambia bodegas o categorías, los filtros y formularios se actualizan sin recargar.
  document.addEventListener("ferrosuite:catalogos", () => cargarCatalogos().catch((e) => FerroSuite.mostrarErrorPagina(e)));

  async function cargarTodo() {
    try {
      if (!catalogosCargados) await cargarCatalogos();
      const nada = Promise.resolve({ datos: [] });
      const [rProductos, rMovimientos, rResumen, rProveedores] = await Promise.all([
        servicio.listar(),
        puede("movimientos.ver") ? servicio.listarMovimientos(20) : nada,
        servicio.resumen(),
        puede("productos.gestionar") ? FerroSuite.proveedoresService.listar() : nada,
      ]);
      FerroSuite.ocultarErrorPagina();
      productos = rProductos.datos;
      const r = rResumen.datos;

      const tarjetas = [
        { titulo: "Productos", valor: r.productos, detalle: "en la base de datos" },
        { titulo: "En riesgo", valor: r.enRiesgo, detalle: r.agotados ? r.agotados + " agotado(s)" : "con stock igual o bajo el mínimo", tono: r.enRiesgo ? "alerta" : "ok" },
      ];
      // Los valores en pesos solo llegan si el rol puede ver finanzas.
      if (r.valorBodega != null) tarjetas.push({ titulo: "Valor en bodega", valor: "$ " + Math.round(r.valorBodega / 1000000) + " M", detalle: pesos(r.valorBodega) + " a precio de compra" });
      if (r.porPagar != null) tarjetas.push({ titulo: "Por pagar", valor: pesos(r.porPagar), detalle: "a proveedores", tono: r.porPagar ? "alerta" : "ok" });
      FerroSuite.tarjetaResumen.render(document.querySelector("#resumen-inventario"), tarjetas);
      FerroSuite.panelAlerta.render(document.querySelector("#alerta-inventario"), productos.filter((p) => p.estado !== "Normal"));
      pintarCatalogo();
      FerroSuite.tabla.render(document.querySelector("#tabla-movimientos"), columnasMovimientos, rMovimientos.datos, "Todavía no hay movimientos.");
      formMovimiento.actualizarOpciones("sku", productos.map((p) => ({ valor: p.sku, texto: p.nombre + " (" + p.unidad.toLowerCase() + ")" })));
      formProducto.actualizarOpciones("nitProveedor", [{ valor: "", texto: "Sin proveedor" }].concat(rProveedores.datos.map((p) => ({ valor: p.nit, texto: p.nombre }))));
    } catch (error) {
      FerroSuite.mostrarErrorPagina(error);
    }
  }

  const formMovimiento = FerroSuite.formulario.render(document.querySelector("#form-movimiento"), {
    textoBoton: "Registrar movimiento",
    campos: [
      { nombre: "sku", etiqueta: "Producto", tipo: "select", opciones: [] },
      { nombre: "tipo", etiqueta: "Tipo", tipo: "select", opciones: lista(["Entrada", "Salida"]) },
      { nombre: "cantidad", etiqueta: "Cantidad", tipo: "number", requerido: true },
      { nombre: "motivo", etiqueta: "Motivo", tipo: "select", opciones: lista(MOTIVOS) },
      { nombre: "bodegaId", etiqueta: "Bodega", tipo: "select", opciones: [] },
    ],
    validar: (d) => servicio.validarMovimiento(d),
    async alEnviar(datos) {
      const r = await servicio.registrarMovimiento(datos);
      await cargarTodo();
      return r.mensaje;
    },
  });

  // El mismo formulario sirve para agregar (RF02) y para editar (RF04).
  let skuEditando = null;

  function terminarEdicion() {
    skuEditando = null;
    formProducto.form.reset();
    formProducto.form.elements.sku.disabled = false;
    formProducto.form.elements.stockActual.disabled = false;
    formProducto.form.elements.bodegaId.disabled = false;
    formProducto.cambiarBoton("Agregar producto");
    tituloProducto.textContent = "Agregar producto";
    botonCancelar.hidden = true;
  }

  const formProducto = FerroSuite.formulario.render(document.querySelector("#form-producto"), {
    textoBoton: "Agregar producto",
    campos: [
      { nombre: "sku", etiqueta: "SKU", placeholder: "FER-0001", requerido: true },
      { nombre: "nombre", etiqueta: "Nombre", requerido: true },
      { nombre: "categoriaId", etiqueta: "Categoría", tipo: "select", opciones: [] },
      { nombre: "unidad", etiqueta: "Unidad de medida", tipo: "select", opciones: lista(UNIDADES) },
      { nombre: "stockActual", etiqueta: "Stock actual", tipo: "number", requerido: true },
      { nombre: "stockMinimo", etiqueta: "Stock mínimo", tipo: "number", requerido: true },
      { nombre: "precioCompra", etiqueta: "Precio de compra (COP)", tipo: "number" },
      { nombre: "nitProveedor", etiqueta: "Proveedor", tipo: "select", opciones: [] },
      { nombre: "bodegaId", etiqueta: "Bodega del stock inicial", tipo: "select", opciones: [] },
    ],
    validar: (d) => servicio.validarProducto(d),
    async alEnviar(datos) {
      const r = skuEditando ? await servicio.actualizar({ ...datos, sku: skuEditando }) : await servicio.crear(datos);
      terminarEdicion();
      await cargarTodo();
      return r.mensaje;
    },
  });

  // Botones Editar y Eliminar de la tabla.
  document.querySelector("#tabla-productos").addEventListener("click", async (e) => {
    const sku = e.target.dataset.editar || e.target.dataset.eliminar;
    if (!sku) return;

    if (e.target.dataset.editar) {
      const p = productos.find((x) => x.sku === sku);
      skuEditando = sku;
      formProducto.llenar(p);
      formProducto.form.elements.sku.disabled = true;
      formProducto.form.elements.stockActual.disabled = true; // el stock cambia solo con movimientos
      formProducto.form.elements.bodegaId.disabled = true;
      formProducto.cambiarBoton("Guardar cambios");
      tituloProducto.textContent = "Editar " + p.nombre;
      botonCancelar.hidden = false;
      formProducto.form.scrollIntoView({ behavior: "smooth" });
      return;
    }

    const boton = e.target.closest("button");
    if (boton.dataset.ocupado === "1") return;
    if (!confirm("¿Eliminar el producto " + sku + "?\nSi tiene historial o existencias se desactivará en lugar de borrarse.")) return;
    await FerroSuite.util.ocupado(boton, async () => {
      try {
        const r = await servicio.eliminar(sku);
        FerroSuite.aviso.mostrar(mensajesCatalogo, r.mensaje, "ok");
        await cargarTodo();
      } catch (error) {
        FerroSuite.aviso.mostrarError(mensajesCatalogo, error);
      }
    });
  });

  botonCancelar.addEventListener("click", terminarEdicion);
  buscador.addEventListener("input", pintarCatalogo);
  filtroCategoria.addEventListener("change", pintarCatalogo);
  filtroRiesgo.addEventListener("change", pintarCatalogo);

  document.querySelector("#btn-restablecer").addEventListener("click", async () => {
    if (!confirm("Se reconstruirá la base de DEMOSTRACIÓN con sus datos sintéticos (la base real no se toca). ¿Continuar?")) return;
    try {
      const r = await servicio.restablecerDatos();
      alert(r.mensaje);
      window.location.href = "login.html";
    } catch (error) {
      FerroSuite.aviso.mostrarError(mensajesCatalogo, error);
    }
  });

  // El asistente avisa cuando crea una orden, para actualizar los números.
  document.addEventListener("ferrosuite:datos-cambiaron", cargarTodo);

  await FerroSuite.sesionLista;
  if (!puede("inventario.ver")) return FerroSuite.sinAcceso();
  cargarTodo();
});
