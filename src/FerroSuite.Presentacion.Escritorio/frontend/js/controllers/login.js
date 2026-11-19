// FerroSuite — Controlador de la pantalla de ingreso.
// Usuario y clave se verifican en el backend (hash de ASP.NET Core Identity). La sesión queda en el backend.
// Si el administrador restableció la clave (o creó el usuario), primero hay que cambiarla.
document.addEventListener("DOMContentLoaded", async () => {
  const zonaIngreso = document.querySelector("#form-login");
  const zonaCambio = document.querySelector("#form-cambio-clave");
  const titulo = document.querySelector("#titulo-login");

  function mostrarCambio() {
    zonaIngreso.hidden = true;
    zonaCambio.hidden = false;
    titulo.textContent = "Cambia tu clave";
    FerroSuite.formulario.render(zonaCambio, {
      textoBoton: "Cambiar clave",
      campos: [
        { nombre: "actual", etiqueta: "Clave actual (la temporal)", tipo: "password", requerido: true },
        { nombre: "nueva", etiqueta: "Clave nueva", tipo: "password", requerido: true },
        { nombre: "repetir", etiqueta: "Repite la clave nueva", tipo: "password", requerido: true },
      ],
      validar(d) {
        const errores = [];
        if (!d.actual) errores.push("Escribe la clave actual.");
        if ((d.nueva || "").length < 10) errores.push("La clave nueva debe tener al menos 10 caracteres.");
        if (d.nueva !== d.repetir) errores.push("Las dos claves nuevas no coinciden.");
        return errores;
      },
      async alEnviar(d) {
        const r = await FerroSuite.api.pedir("sesion.cambiarClave", { actual: d.actual, nueva: d.nueva });
        setTimeout(() => (window.location.href = "index.html"), 700);
        return r.mensaje + " Entrando…";
      },
    });
  }

  FerroSuite.formulario.render(zonaIngreso, {
    textoBoton: "Ingresar",
    campos: [
      { nombre: "usuario", etiqueta: "Usuario", requerido: true },
      { nombre: "clave", etiqueta: "Clave", tipo: "password", requerido: true },
    ],
    validar(datos) {
      const errores = [];
      if (!datos.usuario) errores.push("Escribe tu usuario.");
      if (!datos.clave) errores.push("Escribe tu clave.");
      return errores;
    },
    async alEnviar(datos) {
      const r = await FerroSuite.api.pedir("sesion.iniciar", { usuario: datos.usuario, clave: datos.clave });
      if (r.datos && r.datos.debeCambiarClave) {
        setTimeout(mostrarCambio, 300);
        return r.mensaje;
      }
      setTimeout(() => (window.location.href = "index.html"), 700);
      return r.mensaje + " Entrando…";
    },
  });

  // Aviso visible cuando el programa está en modo demostración (base separada con datos sintéticos).
  FerroSuite.api.pedir("sistema.estado").then((r) => {
    const nota = document.querySelector("#nota-login");
    if (nota && r.datos.modoDemo)
      nota.textContent = "Modo DEMOSTRACIÓN: esta base es distinta de la real y sus datos son sintéticos. Las claves de las cuentas " +
        "de demostración las genera el administrador en Opciones → Generar claves de las cuentas de demostración.";
  }).catch(() => {});

  const sesion = await FerroSuite.sesionLista;
  if (sesion && sesion.debeCambiarClave) mostrarCambio();
});
