// FerroSuite — Controlador de la pantalla de Inteligencia Artificial (pronóstico por producto).
// Todos los valores (historial, pronóstico, días, riesgo, recomendación, fuente) vienen del backend C#, que ejecuta el
// modelo ONNX. Aquí solo se muestran. Cada respuesta se comprueba contra el producto seleccionado y se descartan las
// respuestas de una selección anterior.
document.addEventListener("DOMContentLoaded", async () => {
  const servicio = FerroSuite.pronosticosService;
  const { esc, numero } = FerroSuite.util;
  const $ = (s) => document.querySelector(s);
  const estados = { vacio: $("#ia-vacio"), cargando: $("#ia-cargando"), error: $("#ia-error"), resultado: $("#ia-resultado") };
  const tonosRiesgo = { "Alto": "alerta", "Medio": "alerta", "Bajo": "neutro", "Muy bajo": "ok" };
  let productos = [];
  let seleccionado = null;     // SKU elegido
  let detalle = null;          // última respuesta válida (del SKU elegido)
  let peticion = 0;            // para descartar respuestas viejas

  function mostrar(estado) {
    for (const [k, el] of Object.entries(estados)) el.hidden = k !== estado;
    $(".ia-detalle").setAttribute("aria-busy", estado === "cargando" ? "true" : "false");
  }

  function pintarLista() {
    const filtro = $("#buscar-producto").value.trim().toLowerCase();
    const visibles = productos.filter((p) => !filtro || p.nombre.toLowerCase().includes(filtro) || p.sku.toLowerCase().includes(filtro));
    $("#lista-productos").innerHTML = visibles.length
      ? visibles.map((p) => `
        <button type="button" role="option" class="ia-item${p.sku === seleccionado ? " activo" : ""}" data-sku="${esc(p.sku)}" aria-selected="${p.sku === seleccionado}">
          <span class="ia-item-nombre">${esc(p.nombre)}</span>
          <span class="ia-item-sub">${esc(p.sku)} · stock ${numero(p.stockActual)}</span>
          ${FerroSuite.etiqueta.html("Riesgo " + p.riesgoDesabastecimiento.toLowerCase(), tonosRiesgo[p.riesgoDesabastecimiento])}
        </button>`).join("")
      : '<p class="ia-ayuda">Ningún producto coincide con la búsqueda.</p>';
  }

  async function seleccionar(sku) {
    if (sku !== seleccionado) $("#mensajes-ia").innerHTML = "";   // el aviso de un borrador solo vale para su producto
    seleccionado = sku;
    pintarLista();
    history.replaceState(null, "", "ia.html?sku=" + encodeURIComponent(sku));
    $("#ia-borrador").hidden = true;
    const numeroPeticion = ++peticion;
    $("#ia-cargando-texto").textContent = `Consultando el modelo neuronal para ${sku}…`;
    mostrar("cargando");
    try {
      const r = (await servicio.producto(sku)).datos;
      if (numeroPeticion !== peticion) return;                                     // llegó tarde: ya se eligió otro producto
      if (!r || r.recomendacion.sku !== sku) throw new Error("La respuesta no corresponde al producto seleccionado.");
      detalle = r;
      pintarDetalle(r);
      mostrar("resultado");
    } catch (error) {
      if (numeroPeticion !== peticion) return;
      $("#ia-error-texto").textContent = (error.message || "No se pudo calcular el pronóstico.") +
        (error.errores && error.errores.length ? " " + error.errores.join(" ") : "");
      mostrar("error");
    }
  }

  // "2026-12-03" -> "3 dic 2026" (solo formato; la fecha la calcula el backend).
  function diaCorto(iso) {
    if (!iso) return "—";
    const [a, m, d] = iso.split("-").map(Number);
    return `${d} ${["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"][m - 1]} ${a}`;
  }

  function kpi(titulo, valor, detalleTexto, tono, id) {
    return `<div class="tarjeta-resumen${tono ? " tono-" + tono : ""}" ${id ? `id="${id}"` : ""}>
      <div class="tarjeta-titulo">${esc(titulo)}</div><div class="tarjeta-valor">${valor}</div><div class="tarjeta-detalle">${esc(detalleTexto)}</div></div>`;
  }

  function pintarDetalle(d) {
    const r = d.recomendacion;
    const u = r.unidad.toLowerCase();
    $("#ia-nombre").textContent = r.nombre;
    $("#ia-subtitulo").textContent = `${r.sku} · ${r.categoria || "Sin categoría"} · unidad: ${r.unidad} · datos hasta ${d.fechaCorte}`;
    $("#ia-fuente").innerHTML = r.esRespaldo
      ? FerroSuite.etiqueta.html("Regla de respaldo (stock mínimo)", "alerta")
      : FerroSuite.etiqueta.html("Red neuronal", "ok");
    $("#ia-motivo").hidden = !r.esRespaldo;
    $("#ia-motivo").textContent = r.esRespaldo ? "Por qué no se usó la red neuronal: " + r.motivoRespaldo : "";

    const dias = r.diasHastaAgotarse == null
      ? { valor: "No aplica", texto: "Sin consumo esperado: no se puede calcular cuándo se agota" }
      : { valor: `${numero(r.diasHastaAgotarse)} días`, texto: `aprox. el ${diaCorto(r.fechaEstimadaAgotamiento)}` };
    $("#ia-kpis").innerHTML = [
      kpi("Inventario actual", `${numero(r.stockActual)} <small>${esc(u)}</small>`, `mínimo ${numero(r.stockMinimo)}`, r.stockActual <= r.stockMinimo ? "alerta" : "", "kpi-stock"),
      kpi("Demanda estimada", `${numero(r.consumoDiario)} <small>${esc(u)}/día</small>`, `${numero(d.demandaEstimadaHorizonte)} en los próximos 7 días (${r.fuente.toLowerCase()})`, "", "kpi-demanda"),
      kpi("Se agota en", dias.valor, dias.texto, r.diasHastaAgotarse != null && r.diasHastaAgotarse <= r.diasEntrega ? "alerta" : "", "kpi-dias"),
      kpi("Entrega del proveedor", `${r.diasEntrega} días`, r.proveedor ? r.proveedor : "Sin proveedor (valor por defecto)", "", "kpi-entrega"),
      kpi("Riesgo de desabastecimiento", esc(r.riesgoDesabastecimiento), r.riesgoDescripcion, tonosRiesgo[r.riesgoDesabastecimiento], "kpi-riesgo"),
    ].join("");

    FerroSuite.graficaDemanda.render($("#ia-grafica"), d);

    const puedeCrear = FerroSuite.puede("ordenes.crear");
    const motivoSinBoton = !r.nitProveedor ? "El producto no tiene proveedor asignado: asígnalo en Inventario para poder pedir."
      : r.cantidadSugerida <= 0 && r.urgencia !== "Sin acción" ? "Lo ya pedido cubre la necesidad."
      : r.cantidadSugerida <= 0 ? "No hace falta pedir por ahora." : !puedeCrear ? "Tu rol no puede crear órdenes." : "";
    $("#ia-recomendacion").innerHTML = `
      <p class="ia-urgencia">${FerroSuite.etiqueta.html(r.urgencia, tonosRiesgo[r.riesgoDesabastecimiento])}
        ${r.cantidadSugerida > 0 ? `Pedir <strong>${numero(r.cantidadSugerida)} ${esc(u)}</strong>` : ""}
        ${r.enCamino > 0 ? `<span class="ia-ayuda">(ya hay ${numero(r.enCamino)} en órdenes sin recibir)</span>` : ""}</p>
      <p class="ia-explicacion">${esc(r.explicacion)}</p>
      ${r.puedePrepararOrden && puedeCrear
        ? '<button type="button" id="btn-borrador" class="btn btn-primary">Generar borrador de orden</button>'
        : `<p class="ia-ayuda">${esc(motivoSinBoton)}</p>`}`;
    const btn = $("#btn-borrador");
    if (btn) btn.addEventListener("click", abrirBorrador);

    const h = d.resumen;
    $("#ia-historial").innerHTML = `
      <dt>Primer movimiento</dt><dd>${diaCorto(h.primerMovimiento)}</dd>
      <dt>Días de historia</dt><dd>${h.diasDeHistoria}</dd>
      <dt>Días con venta (90 días)</dt><dd>${h.diasConVenta}</dd>
      <dt>Días agotado (90 días)</dt><dd>${h.diasAgotado}</dd>
      <dt>Días con dato en la ventana del modelo</dt><dd>${h.diasConDatoEnVentana} de 28 <small>(mín. 14)</small></dd>
      <dt>Vendido en los últimos 30 días</dt><dd>${numero(h.vendidoUltimos30Dias)} ${esc(u)}</dd>
      <dt>Suficiente para la red neuronal</dt><dd>${h.suficienteParaModelo ? "Sí" : "No"}</dd>`;
    $("#ia-proyeccion").innerHTML = `<table class="data-table ia-tabla-proyeccion"><thead><tr><th>Día</th><th>Consumo estimado</th><th>Stock al cierre</th></tr></thead><tbody>${
      d.pronostico.map((p) => `<tr><td>${p.fecha}</td><td>${numero(p.consumoEstimado)}</td><td>${numero(p.stockProyectado)}</td></tr>`).join("")}</tbody></table>`;
    $("#ia-nota").textContent = r.esRespaldo
      ? "Resultado de la regla de respaldo: vendido en 30 días ÷ 30 y stock mínimo."
      : `Resultado de la red neuronal (${d.modelo.descripcion}). Es una ayuda para decidir; revisa la cantidad antes de pedir.`;
  }

  function abrirBorrador() {
    const r = detalle.recomendacion;
    $("#borrador-producto").textContent = `${r.nombre} (${r.sku})`;
    $("#borrador-proveedor").textContent = `${r.proveedor} · NIT ${r.nitProveedor} · entrega en ${r.diasEntrega} días`;
    $("#borrador-cantidad").value = r.cantidadSugerida;
    $("#mensajes-borrador").innerHTML = "";
    $("#ia-borrador").hidden = false;
    $("#borrador-cantidad").focus();
  }

  $("#btn-crear-borrador").addEventListener("click", async () => {
    const r = detalle.recomendacion;
    const boton = $("#btn-crear-borrador");
    boton.disabled = true;
    boton.textContent = "Creando…";
    try {
      const resp = await servicio.generarBorrador(r.sku, r.nitProveedor, $("#borrador-cantidad").value);
      FerroSuite.aviso.mostrar($("#mensajes-ia"), [resp.mensaje, "Puedes revisarla en Órdenes de compra."], "ok");
      $("#ia-borrador").hidden = true;               // evita crear la misma orden dos veces
      const sku = r.sku;
      setTimeout(() => { if (seleccionado === sku) seleccionar(sku); }, 1200);   // recalcula: ahora la cantidad aparece "en camino"
    } catch (error) {
      FerroSuite.aviso.mostrarError($("#mensajes-borrador"), error);
    } finally {
      boton.disabled = false;
      boton.textContent = "Crear borrador";
    }
  });
  $("#btn-cancelar-borrador").addEventListener("click", () => ($("#ia-borrador").hidden = true));
  $("#btn-reintentar").addEventListener("click", () => seleccionado && seleccionar(seleccionado));
  $("#buscar-producto").addEventListener("input", pintarLista);
  $("#lista-productos").addEventListener("click", (e) => {
    const b = e.target.closest("[data-sku]");
    if (b) seleccionar(b.dataset.sku);
  });

  await FerroSuite.sesionLista;
  if (!FerroSuite.puede("pronosticos.ver")) return FerroSuite.sinAcceso();
  mostrar("vacio");
  $("#lista-productos").innerHTML = '<p class="ia-ayuda">Cargando productos…</p>';
  try {
    const r = (await servicio.reposicion()).datos;
    productos = r.recomendaciones;
    $("#estado-modelo").innerHTML = r.modelo.disponible && !r.falloModelo
      ? FerroSuite.etiqueta.html("Modelo neuronal activo", "ok") + ` <span>${esc(r.modelo.descripcion)}</span>`
      : FerroSuite.etiqueta.html("Modelo no disponible: se usa la regla de stock mínimo", "alerta") +
        ` <span>${esc(r.falloModelo ? "El modelo falló al calcular." : r.modelo.motivo || "")} El inventario funciona con normalidad.</span>`;
    pintarLista();
    const pedido = new URLSearchParams(window.location.search).get("sku");
    if (pedido && productos.some((p) => p.sku === pedido)) seleccionar(pedido);
  } catch (error) {
    $("#lista-productos").innerHTML = "";
    FerroSuite.mostrarErrorPagina(error);
  }
});
