// FerroSuite — Componente: gráfica de demanda histórica y pronosticada (SVG, sin librerías externas).
// SOLO DIBUJA lo que llega del backend (historial diario y pronóstico calculado en C# con el modelo ONNX).
// No calcula ningún pronóstico.
window.FerroSuite = window.FerroSuite || {};

FerroSuite.graficaDemanda = {
  /**
   * @param contenedor elemento donde se dibuja
   * @param d detalle de pronosticos.producto: historial, pronostico, ventanaModeloDesde, recomendacion
   */
  render(contenedor, d) {
    const esc = FerroSuite.util.esc;
    const r = d.recomendacion;
    const hist = d.historial;
    const futuro = d.pronostico;
    const dias = hist.length + futuro.length;
    const W = 960, H = 340, m = { izq: 56, der: 18, arr: 26, aba: 46 };
    const ancho = W - m.izq - m.der, alto = H - m.arr - m.aba;
    const paso = ancho / dias;
    const valores = hist.map((h) => h.demanda || 0).concat(futuro.map((f) => f.consumoEstimado), [r.consumoRegla30Dias]);
    const tope = FerroSuite.graficaDemanda.escalaBonita(Math.max(1, ...valores));
    const y = (v) => m.arr + alto - (v / tope) * alto;
    const x = (i) => m.izq + i * paso;
    const fecha = (iso) => iso.slice(8, 10) + "/" + iso.slice(5, 7);
    const partes = [];

    // Fondo de la ventana que vio el modelo (últimos 28 días).
    const iVentana = hist.findIndex((h) => h.enVentanaModelo);
    if (iVentana >= 0) {
      partes.push(`<rect x="${x(iVentana)}" y="${m.arr}" width="${(hist.length - iVentana) * paso}" height="${alto}" class="g-ventana"/>`);
      partes.push(`<text x="${x(iVentana) + 2}" y="${m.arr - 8}" class="g-nota">Ventana que vio el modelo (28 días)</text>`);
    }
    // Zona del pronóstico.
    partes.push(`<rect x="${x(hist.length)}" y="${m.arr}" width="${futuro.length * paso}" height="${alto}" class="g-futuro"/>`);

    // Ejes y cuadrícula.
    for (let k = 0; k <= 4; k++) {
      const v = (tope * k) / 4;
      partes.push(`<line x1="${m.izq}" x2="${W - m.der}" y1="${y(v)}" y2="${y(v)}" class="g-grilla"/>`);
      partes.push(`<text x="${m.izq - 8}" y="${y(v) + 4}" class="g-eje" text-anchor="end">${FerroSuite.util.numero(Math.round(v * 100) / 100)}</text>`);
    }
    const cadaCuanto = Math.max(1, Math.round(dias / 10));
    hist.concat(futuro).forEach((p, i) => {
      if (i % cadaCuanto === 0) partes.push(`<text x="${x(i) + paso / 2}" y="${H - m.aba + 18}" class="g-eje" text-anchor="middle">${fecha(p.fecha)}</text>`);
    });

    // Historial: barras por día; agotado y sin historia como bandas (la demanda no se observó).
    hist.forEach((h, i) => {
      const titulo = `${h.fecha}: ` + ({ venta: `${h.demanda} vendidos`, sin_venta: "sin ventas", agotado: "agotado (demanda no observada)", sin_historia: "sin historial" })[h.estado];
      if (h.estado === "agotado" || h.estado === "sin_historia") {
        partes.push(`<rect x="${x(i)}" y="${m.arr}" width="${paso}" height="${alto}" class="g-${h.estado}"><title>${esc(titulo)}</title></rect>`);
      } else {
        const v = h.demanda || 0;
        partes.push(`<rect x="${x(i) + paso * 0.15}" y="${y(v)}" width="${paso * 0.7}" height="${Math.max(v > 0 ? 1 : 0, m.arr + alto - y(v))}" class="g-barra"><title>${esc(titulo)}</title></rect>`);
      }
    });

    // Referencia: regla actual (vendido en 30 días / 30) sobre los últimos 30 días.
    const i30 = Math.max(0, hist.length - 30);
    partes.push(`<line x1="${x(i30)}" x2="${x(hist.length)}" y1="${y(r.consumoRegla30Dias)}" y2="${y(r.consumoRegla30Dias)}" class="g-regla"><title>Regla de 30 días: ${r.consumoRegla30Dias}/día</title></line>`);

    // Pronóstico (promedio diario de los 7 días siguientes).
    const claseFuturo = r.esRespaldo ? "g-pronostico-respaldo" : "g-pronostico";
    const yf = y(futuro.length ? futuro[0].consumoEstimado : 0);
    partes.push(`<rect x="${x(hist.length)}" y="${yf}" width="${futuro.length * paso}" height="${m.arr + alto - yf}" class="${claseFuturo}-area"/>`);
    partes.push(`<line x1="${x(hist.length)}" x2="${x(dias)}" y1="${yf}" y2="${yf}" class="${claseFuturo}"><title>${esc(r.fuente)}: ${r.consumoDiario}/día</title></line>`);
    partes.push(`<line x1="${x(hist.length)}" x2="${x(hist.length)}" y1="${m.arr}" y2="${m.arr + alto}" class="g-hoy"/>`);
    partes.push(`<text x="${W - m.der}" y="${m.arr - 8}" class="g-nota" text-anchor="end">Pronóstico →</text>`);
    if (!hist.some((h) => h.estado === "venta")) {
      partes.push(`<rect x="${m.izq + ancho * 0.2}" y="${m.arr + alto / 2 - 24}" width="${ancho * 0.6}" height="48" rx="6" class="g-mensaje-fondo"/>`);
      partes.push(`<text x="${m.izq + ancho / 2}" y="${m.arr + alto / 2 + 5}" class="g-mensaje" text-anchor="middle">Sin ventas registradas en los últimos ${hist.length} días: no hay historial que graficar</text>`);
    }

    const resumen = `Demanda diaria de ${r.nombre}: ${hist.filter((h) => h.estado === "venta").length} días con venta en ${hist.length} días; ` +
      `pronóstico ${r.consumoDiario} por día (${r.fuente}).`;
    contenedor.innerHTML = `
      <svg viewBox="0 0 ${W} ${H}" class="grafica-demanda" role="img" aria-label="${esc(resumen)}">${partes.join("")}</svg>
      <ul class="g-leyenda">
        <li><span class="g-muestra g-barra"></span>Venta diaria registrada</li>
        <li><span class="g-muestra g-agotado"></span>Agotado (demanda no observada)</li>
        <li><span class="g-muestra g-sin_historia"></span>Sin historial</li>
        <li><span class="g-muestra g-linea g-regla"></span>Regla actual: vendido en 30 días ÷ 30 (${FerroSuite.util.numero(r.consumoRegla30Dias)}/día)</li>
        <li><span class="g-muestra g-linea ${claseFuturo}"></span>${esc(r.fuente)}: ${FerroSuite.util.numero(r.consumoDiario)}/día (promedio de los próximos 7 días)</li>
      </ul>`;
  },

  // Tope del eje vertical redondeado (1, 2, 2.5, 5 × 10^n).
  escalaBonita(v) {
    const e = Math.pow(10, Math.floor(Math.log10(v)));
    for (const f of [1, 2, 2.5, 5, 10]) if (v <= f * e) return f * e;
    return 10 * e;
  },
};
