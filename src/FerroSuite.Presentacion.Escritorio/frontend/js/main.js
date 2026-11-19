// FerroSuite — Arranque del frontend.
// Dibuja los componentes comunes (encabezado y pie) en cada pantalla y deja
// listo el menú móvil. Cada pantalla carga después su propio controlador.
window.FerroSuite = window.FerroSuite || {};

document.addEventListener("DOMContentLoaded", () => {
  const pagina = document.body.dataset.pagina || "";

  const zonaEncabezado = document.querySelector('[data-componente="encabezado"]');
  if (zonaEncabezado) {
    zonaEncabezado.innerHTML = FerroSuite.encabezado.html(pagina);
    FerroSuite.encabezado.activarMenuMovil(zonaEncabezado);
  }

  const zonaPie = document.querySelector('[data-componente="pie"]');
  if (zonaPie) zonaPie.innerHTML = FerroSuite.pie.html();

  // Las pantallas esperan esta promesa antes de pedir datos, para pedir solo lo que el rol permite.
  FerroSuite.sesionLista = zonaEncabezado && FerroSuite.api && FerroSuite.api.disponible()
    ? FerroSuite.mostrarSesion(zonaEncabezado, pagina)
    : Promise.resolve(null);
});

// ¿El rol de la sesión tiene alguno de estos permisos? Solo sirve para mostrar u ocultar cosas en pantalla:
// el backend vuelve a comprobar cada operación y rechaza las que el rol no permite.
FerroSuite.puede = function (...permisos) {
  const propios = (FerroSuite.sesion && FerroSuite.sesion.permisos) || [];
  return permisos.some((p) => propios.includes(p));
};

// Oculta los elementos con data-permiso="a b" si el rol no tiene ninguno de esos permisos.
FerroSuite.aplicarPermisos = function (raiz) {
  (raiz || document).querySelectorAll("[data-permiso]").forEach((el) => {
    const permitido = FerroSuite.puede(...el.dataset.permiso.split(/\s+/));
    if (el.id === "btn-restablecer") return; // además depende del modo Demo (lo decide la pantalla)
    el.hidden = !permitido;
  });
  document.body.classList.add("permisos-listos");
};

// Para pantallas a las que el rol no tiene acceso: muestra un aviso en lugar del contenido.
FerroSuite.sinAcceso = function () {
  const main = document.querySelector("main");
  if (!main) return;
  // Si no hubo respuesta del backend, el problema es la comunicación, no el rol.
  if (FerroSuite.errorComunicacion) {
    main.innerHTML = '<section class="section"><div class="container"><h2>No hay comunicación con el backend</h2>' +
      "<p>No se pudo consultar la sesión. Revisa el programa y vuelve a intentar.</p>" +
      '<p><button type="button" class="btn btn-primary" onclick="location.reload()">Reintentar</button></p></div></section>';
    return;
  }
  main.innerHTML = '<section class="section"><div class="container"><h2>Sin acceso</h2>' +
    "<p>Tu rol no tiene acceso a esta pantalla. Si lo necesitas, pídeselo al administrador.</p></div></section>";
};

// Pregunta al backend quién tiene la sesión. Sin sesión, las pantallas (salvo la de ingreso) llevan a ingresar.
FerroSuite.mostrarSesion = async function (zona, pagina) {
  try {
    const sesion = (await FerroSuite.api.pedir("sesion.actual")).datos;
    FerroSuite.sesion = sesion || null;
    const item = zona.querySelector(".nav-ingresar");
    if (!sesion || (sesion.debeCambiarClave && pagina !== "login")) {
      if (pagina !== "login") window.location.href = "login.html";
      return FerroSuite.sesion;
    }
    if (item) {
      item.innerHTML = `<span class="nav-usuario">${FerroSuite.util.esc(sesion.nombreCompleto)} · ${FerroSuite.util.esc(sesion.rolNombre)}</span>
        <button type="button" class="btn-chico" id="btn-salir">Salir</button>`;
      item.querySelector("#btn-salir").addEventListener("click", async () => {
        await FerroSuite.api.pedir("sesion.cerrar").catch(() => {});
        window.location.href = "login.html";
      });
    }
    FerroSuite.aplicarPermisos();
    document.dispatchEvent(new CustomEvent("ferrosuite:sesion", { detail: sesion }));
    return sesion;
  } catch (e) {
    // Sin respuesta del backend: se avisa en la pantalla (no es un problema de permisos).
    FerroSuite.errorComunicacion = e;
    if (pagina !== "login") FerroSuite.mostrarErrorPagina(e);
    return null;
  }
};

// Muestra un aviso de error arriba de la pantalla (por ejemplo, si el backend no responde).
FerroSuite.mostrarErrorPagina = function (error) {
  let zona = document.querySelector("#aviso-pagina");
  if (!zona) {
    zona = document.createElement("div");
    zona.id = "aviso-pagina";
    zona.className = "container aviso-pagina";
    document.querySelector("main").prepend(zona);
  }
  FerroSuite.aviso.mostrarError(zona, error);
};

FerroSuite.ocultarErrorPagina = function () {
  const zona = document.querySelector("#aviso-pagina");
  if (zona) zona.remove();
};
