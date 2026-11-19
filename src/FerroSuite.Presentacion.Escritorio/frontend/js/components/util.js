// FerroSuite — Utilidades compartidas por los componentes.
window.FerroSuite = window.FerroSuite || {};

FerroSuite.util = {
  // Escapa texto antes de ponerlo dentro de HTML.
  esc(valor) {
    return String(valor ?? "").replace(/[&<>"']/g, (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])
    );
  },

  // 1234.5 -> "1.234,5"
  numero(valor) {
    return Number(valor).toLocaleString("es-CO");
  },

  // 1234567 -> "$ 1.234.567"
  pesos(valor) {
    if (valor === null || valor === undefined) return "—";
    return "$ " + Math.round(valor).toLocaleString("es-CO");
  },

  // (46, "Unidad") -> "46 unidades"
  cantidad(valor, unidad) {
    let u = unidad.toLowerCase();
    if (valor !== 1) u = u === "galón" ? "galones" : u === "unidad" ? "unidades" : u + "s";
    return FerroSuite.util.numero(valor) + " " + u;
  },

  /**
   * Ejecuta una operación con el botón desactivado hasta que el backend responda (evita envíos repetidos por
   * doble clic). Si el botón ya está ocupado, no hace nada.
   */
  async ocupado(boton, operacion) {
    if (boton && boton.dataset.ocupado === "1") return undefined;
    const texto = boton ? boton.textContent : "";
    if (boton) { boton.dataset.ocupado = "1"; boton.disabled = true; boton.setAttribute("aria-busy", "true"); boton.textContent = "Procesando…"; }
    try {
      return await operacion();
    } finally {
      if (boton && boton.isConnected) { boton.disabled = false; boton.removeAttribute("aria-busy"); boton.textContent = texto; }
      if (boton) delete boton.dataset.ocupado;
    }
  },

  fecha(iso) {
    return new Date(iso).toLocaleString("es-CO", { dateStyle: "short", timeStyle: "short" });
  },
};
