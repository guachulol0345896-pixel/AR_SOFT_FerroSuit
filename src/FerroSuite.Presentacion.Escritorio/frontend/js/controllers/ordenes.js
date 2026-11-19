// FerroSuite — Controlador de Órdenes de compra.
// Los botones dependen del rol: aprobar/rechazar (administrador), recibir (bodega), pagar (contador).
document.addEventListener("DOMContentLoaded", async () => {
  const servicio = FerroSuite.ordenesService;
  const puede = FerroSuite.puede;
  const { esc, numero, pesos } = FerroSuite.util;
  const zonaResumen = document.querySelector("#resumen-ordenes");
  const zonaTabla = document.querySelector("#tabla-ordenes");
  const mensajesTabla = document.querySelector("#mensajes-ordenes");
  const tonoEstado = { "Aprobada": "ok", "Recibida": "ok", "Rechazada": "neutro" };

  const columnas = [
    { titulo: "Código", valor: (o) => esc(o.codigo) },
    { titulo: "Producto", valor: (o) => esc(o.nombreProducto) },
    { titulo: "Proveedor", valor: (o) => esc(o.nombreProveedor) },
    { titulo: "Cantidad", valor: (o) => numero(o.cantidad) },
    { titulo: "Valor", valor: (o) => pesos(o.valor) },
    { titulo: "Estado", valor: (o) => FerroSuite.etiqueta.html(o.estado, tonoEstado[o.estado] || "alerta") },
    {
      titulo: "Pago",
      valor: (o) => {
        if (o.pagada) return FerroSuite.etiqueta.html("Pagada", "ok");
        if (!o.vencePago) return "—";
        const vence = new Date(o.vencePago);
        const texto = "Vence " + vence.toLocaleDateString("es-CO", { day: "2-digit", month: "2-digit" });
        return FerroSuite.etiqueta.html(texto, vence < new Date() ? "alerta" : "neutro");
      },
    },
    {
      titulo: "Acción",
      valor: (o) => {
        const c = esc(o.codigo);
        const botones = [];
        if (o.estado === "Pendiente de aprobación" && puede("ordenes.aprobar"))
          botones.push(`<button type="button" class="btn-chico" data-aprobar="${c}">Aprobar</button>`,
                       `<button type="button" class="btn-chico btn-peligro" data-rechazar="${c}">Rechazar</button>`);
        if (o.estado === "Aprobada" && puede("ordenes.recibir")) botones.push(`<button type="button" class="btn-chico" data-recibir="${c}">Recibir mercancía</button>`);
        if ((o.estado === "Aprobada" || o.estado === "Recibida") && !o.pagada && puede("ordenes.pagar")) botones.push(`<button type="button" class="btn-chico" data-pagar="${c}">Marcar pagada</button>`);
        return botones.join(" ") || "—";
      },
    },
  ];

  async function cargar() {
    try {
      const crear = puede("ordenes.crear"), nada = Promise.resolve({ datos: [] });
      const [rOrdenes, rProductos, rProveedores] = await Promise.all([
        servicio.listar(), crear ? FerroSuite.inventarioService.listar() : nada, crear ? FerroSuite.proveedoresService.listar() : nada,
      ]);
      FerroSuite.ocultarErrorPagina();
      const ordenes = rOrdenes.datos;

      let porPagar = 0;
      for (const o of ordenes) if ((o.estado === "Aprobada" || o.estado === "Recibida") && !o.pagada) porPagar += o.valor || 0;
      const pendientes = ordenes.filter((o) => o.estado === "Pendiente de aprobación").length;
      const tarjetas = [
        { titulo: "Órdenes", valor: ordenes.length, detalle: "registradas" },
        { titulo: "Pendientes", valor: pendientes, detalle: "esperan aprobación del dueño", tono: pendientes ? "alerta" : "ok" },
      ];
      if (puede("finanzas.ver")) tarjetas.push({ titulo: "Por pagar", valor: pesos(porPagar), detalle: "a proveedores (plazo de 15 días)", tono: porPagar ? "alerta" : "ok" });
      FerroSuite.tarjetaResumen.render(zonaResumen, tarjetas);
      FerroSuite.tabla.render(zonaTabla, columnas, ordenes, "Todavía no hay órdenes de compra.");

      // Los productos en riesgo aparecen primero en la lista.
      const productos = rProductos.datos.slice().sort((a, b) => (a.estado === "Normal") - (b.estado === "Normal"));
      form.actualizarOpciones("sku", productos.map((p) => ({ valor: p.sku, texto: (p.estado !== "Normal" ? "⚠ " : "") + p.nombre + " (" + p.sku + ")" })));
      form.actualizarOpciones("nitProveedor", rProveedores.datos.map((p) => ({ valor: p.nit, texto: p.nombre + " · entrega en " + p.diasEntrega + " días" })));
    } catch (error) {
      FerroSuite.mostrarErrorPagina(error);
    }
  }

  zonaTabla.addEventListener("click", async (e) => {
    const boton = e.target.closest("button");
    if (!boton) return;
    const { aprobar, pagar, rechazar, recibir } = boton.dataset;
    if (!aprobar && !pagar && !rechazar && !recibir) return;
    if (boton.dataset.ocupado === "1") return;
    if (rechazar && !confirm("¿Rechazar la orden " + rechazar + "?")) return;
    if (recibir && !confirm("¿Confirmas que llegó la mercancía de la orden " + recibir + "? Se sumará al inventario.")) return;
    // El botón queda ocupado hasta que responda el backend: un doble clic no envía dos solicitudes.
    await FerroSuite.util.ocupado(boton, async () => {
      try {
        let r;
        if (aprobar) r = await servicio.aprobar(aprobar);
        else if (rechazar) r = await servicio.rechazar(rechazar);
        else if (recibir) r = await servicio.recibir(recibir, null); // primera bodega activa (Bodega principal)
        else r = await servicio.pagar(pagar);
        FerroSuite.aviso.mostrar(mensajesTabla, r.mensaje, "ok");
        await cargar();
      } catch (error) {
        FerroSuite.aviso.mostrarError(mensajesTabla, error);
      }
    });
  });

  const form = FerroSuite.formulario.render(document.querySelector("#form-orden"), {
    textoBoton: "Generar orden",
    campos: [
      { nombre: "sku", etiqueta: "Producto", tipo: "select", opciones: [] },
      { nombre: "nitProveedor", etiqueta: "Proveedor", tipo: "select", opciones: [] },
      { nombre: "cantidad", etiqueta: "Cantidad a pedir", tipo: "number", requerido: true },
    ],
    validar: (d) => servicio.validar(d),
    async alEnviar(datos) {
      const r = await servicio.crear(datos);
      await cargar();
      return r.mensaje;
    },
  });

  await FerroSuite.sesionLista;
  if (!puede("ordenes.ver")) return FerroSuite.sinAcceso();
  await cargar();
  // Viene de "Preparar orden" en Pronósticos: se llenan los campos, pero la orden solo se crea si el usuario pulsa "Generar orden".
  const q = new URLSearchParams(window.location.search);
  if (q.get("origen") === "pronostico" && puede("ordenes.crear")) {
    form.form.elements.sku.value = q.get("sku") || "";
    form.form.elements.nitProveedor.value = q.get("nit") || "";
    form.form.elements.cantidad.value = q.get("cantidad") || "";
    FerroSuite.aviso.mostrar(form.form.parentElement.querySelector(".form-mensajes"),
      "Datos traídos del pronóstico. Revísalos y pulsa \"Generar orden\": quedará pendiente de aprobación del administrador.", "ok");
    form.form.scrollIntoView({ behavior: "smooth" });
  }
});
