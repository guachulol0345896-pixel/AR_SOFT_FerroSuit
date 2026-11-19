// FerroSuite — Controlador de Pronósticos: muestra las recomendaciones de reposición del backend.
// "Preparar orden" solo lleva los datos al formulario de órdenes: la orden queda pendiente de aprobación.
document.addEventListener("DOMContentLoaded", async () => {
  const { esc, numero } = FerroSuite.util;
  const zonaTabla = document.querySelector("#tabla-pronostico");
  const soloAccion = document.querySelector("#solo-accion");
  const mensajes = document.querySelector("#mensajes-pronostico");
  let ultimo = null;
  const tonos = { "Urgente": "alerta", "Pedir ahora": "alerta", "Programar pedido": "neutro", "Sin acción": "ok" };

  const columnas = [
    { titulo: "Producto", valor: (r) => `<strong><a href="ia.html?sku=${encodeURIComponent(r.sku)}" title="Ver historial y gráfica">${esc(r.nombre)}</a></strong><span class="sub">${esc(r.sku)} · ${esc(r.categoria || "")}</span>` },
    { titulo: "Stock", valor: (r) => numero(r.stockActual) + `<span class="sub">mínimo ${numero(r.stockMinimo)}</span>` },
    {
      titulo: "Consumo diario",
      valor: (r) => `${numero(r.consumoDiario)} ${esc(r.unidad.toLowerCase())}/día` +
        `<span class="sub">${r.esRespaldo ? FerroSuite.etiqueta.html("Regla de stock mínimo", "neutro") : FerroSuite.etiqueta.html("Red neuronal", "ok")}</span>`,
    },
    {
      titulo: "Se agota en",
      valor: (r) => (r.diasHastaAgotarse == null ? "—" : `${numero(r.diasHastaAgotarse)} días`) +
        (r.fechaEstimadaAgotamiento ? `<span class="sub">${esc(r.fechaEstimadaAgotamiento)}</span>` : ""),
    },
    { titulo: "Entrega", valor: (r) => `${r.diasEntrega} días<span class="sub">${esc(r.proveedor || "Sin proveedor")}</span>` },
    { titulo: "Recomendación", valor: (r) => FerroSuite.etiqueta.html(r.urgencia, tonos[r.urgencia]) + (r.cantidadSugerida > 0 ? `<span class="sub">pedir ${numero(r.cantidadSugerida)}</span>` : "") + (r.enCamino > 0 ? `<span class="sub">en camino ${numero(r.enCamino)}</span>` : "") },
    {
      titulo: "Por qué",
      valor: (r) => `<span class="explicacion">${esc(r.explicacion)}</span>` + (r.motivoRespaldo ? `<span class="sub motivo-respaldo">Respaldo: ${esc(r.motivoRespaldo)}</span>` : ""),
    },
    {
      titulo: "",
      valor: (r) => r.puedePrepararOrden && FerroSuite.puede("ordenes.crear")
        ? `<button type="button" class="btn-chico" data-preparar="${esc(r.sku)}">Preparar orden</button>` : "",
    },
  ];

  function pintar() {
    if (!ultimo) return;
    const filas = ultimo.recomendaciones.filter((r) => !soloAccion.checked || r.urgencia !== "Sin acción");
    FerroSuite.tabla.render(zonaTabla, columnas, filas, "Ningún producto requiere acción en este momento.");
  }

  async function cargar() {
    zonaTabla.innerHTML = "<p>Calculando pronóstico con la red neuronal…</p>";
    try {
      const r = (await FerroSuite.pronosticosService.reposicion()).datos;
      ultimo = r;
      FerroSuite.ocultarErrorPagina();
      const estado = document.querySelector("#estado-modelo");
      estado.innerHTML = r.modelo.disponible && !r.falloModelo
        ? FerroSuite.etiqueta.html("Modelo neuronal activo", "ok") + ` <span>${esc(r.modelo.descripcion)}</span>`
        : FerroSuite.etiqueta.html("Modelo no disponible: se usa la regla de stock mínimo", "alerta") +
          ` <span>${esc(r.falloModelo ? "El modelo falló al calcular." : r.modelo.motivo || "")} El inventario funciona con normalidad.</span>`;
      const urgentes = r.recomendaciones.filter((x) => x.urgencia === "Urgente").length;
      const pedir = r.recomendaciones.filter((x) => x.urgencia === "Pedir ahora").length;
      FerroSuite.tarjetaResumen.render(document.querySelector("#resumen-pronostico"), [
        { titulo: "Horizonte", valor: "7 días", detalle: `del ${r.horizonteDesde} al ${r.horizonteHasta} (datos hasta ${r.fechaCorte})` },
        { titulo: "Con red neuronal", valor: r.porRedNeuronal, detalle: "productos pronosticados por el modelo", tono: "ok" },
        { titulo: "Con regla de respaldo", valor: r.porRespaldo, detalle: "sin historial suficiente o sin modelo", tono: r.porRespaldo ? "alerta" : "ok" },
        { titulo: "Urgentes / pedir ahora", valor: `${urgentes} / ${pedir}`, detalle: "se agotan antes o cerca de la entrega", tono: urgentes + pedir ? "alerta" : "ok" },
      ]);
      pintar();
    } catch (error) {
      zonaTabla.innerHTML = "";
      FerroSuite.mostrarErrorPagina(error);
    }
  }

  zonaTabla.addEventListener("click", (e) => {
    const sku = e.target.dataset.preparar;
    if (!sku) return;
    const r = ultimo.recomendaciones.find((x) => x.sku === sku);
    // Solo se pasan los datos al formulario: la orden NO se crea hasta que el usuario la genera, y queda pendiente de aprobación.
    const q = new URLSearchParams({ sku: r.sku, nit: r.nitProveedor, cantidad: String(r.cantidadSugerida), origen: "pronostico" });
    window.location.href = "ordenes.html?" + q.toString();
  });
  soloAccion.addEventListener("change", pintar);
  document.querySelector("#btn-recalcular").addEventListener("click", cargar);

  await FerroSuite.sesionLista;
  if (!FerroSuite.puede("pronosticos.ver")) return FerroSuite.sinAcceso();
  cargar();
});
