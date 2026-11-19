// FerroSuite — Controlador de Usuarios y auditoría (administrador).
document.addEventListener("DOMContentLoaded", async () => {
  const servicio = FerroSuite.usuariosService;
  const { esc, fecha, ocupado } = FerroSuite.util;
  let usuarios = [];
  let editando = null; // usuarioId que se edita; null = crear
  const ayudaCrear = document.querySelector("#ayuda-form-usuario")?.textContent || "";
  const mensajes = document.querySelector("#mensajes-usuarios");

  const bloqueado = (u) => u.bloqueadoHasta && new Date(u.bloqueadoHasta) > new Date();
  const columnas = [
    { titulo: "Usuario", valor: (u) => `<strong>${esc(u.nombreUsuario)}</strong><span class="sub">${esc(u.nombreCompleto)}</span>` },
    { titulo: "Rol", valor: (u) => esc(u.rolNombre) },
    {
      titulo: "Estado",
      valor: (u) => [
        FerroSuite.etiqueta.html(u.activo ? "Activo" : "Inactivo", u.activo ? "ok" : "neutro"),
        bloqueado(u) ? FerroSuite.etiqueta.html("Bloqueado", "alerta") : "",
        u.debeCambiarClave ? FerroSuite.etiqueta.html("Debe cambiar la clave", "neutro") : "",
        !u.tieneClave ? FerroSuite.etiqueta.html("Sin clave", "neutro") : "",
      ].join(" "),
    },
    { titulo: "Último ingreso", valor: (u) => (u.ultimoIngreso ? fecha(u.ultimoIngreso) : "—") },
    {
      titulo: "Acciones",
      valor: (u) => {
        const id = u.usuarioId;
        return [
          `<button type="button" class="btn-chico" data-editar="${id}">Editar</button>`,
          u.activo ? `<button type="button" class="btn-chico btn-peligro" data-desactivar="${id}">Desactivar</button>`
                   : `<button type="button" class="btn-chico" data-activar="${id}">Activar</button>`,
          `<button type="button" class="btn-chico" data-restablecer="${id}">Restablecer clave</button>`,
          bloqueado(u) ? `<button type="button" class="btn-chico" data-desbloquear="${id}">Desbloquear</button>` : "",
        ].join(" ");
      },
    },
  ];
  const columnasAuditoria = [
    { titulo: "Fecha", valor: (e) => fecha(e.fecha) },
    { titulo: "Usuario", valor: (e) => esc(e.nombreUsuario) + (e.rolCodigo ? `<span class="sub">${esc(e.rolCodigo)}</span>` : "") },
    { titulo: "Operación", valor: (e) => esc(e.accion) },
    { titulo: "Resultado", valor: (e) => FerroSuite.etiqueta.html(e.resultado, e.resultado === "Exitoso" ? "ok" : "alerta") },
    { titulo: "Detalle", valor: (e) => esc([e.objeto, e.detalle].filter(Boolean).join(" · ")) },
  ];

  async function cargar() {
    try {
      const [rUsuarios, rAuditoria] = await Promise.all([servicio.listar(), servicio.auditoria(100)]);
      FerroSuite.ocultarErrorPagina();
      usuarios = rUsuarios.datos;
      FerroSuite.tabla.render(document.querySelector("#tabla-usuarios"), columnas, usuarios, "No hay usuarios.");
      FerroSuite.tabla.render(document.querySelector("#tabla-auditoria"), columnasAuditoria, rAuditoria.datos, "Sin eventos.");
    } catch (error) {
      FerroSuite.mostrarErrorPagina(error);
    }
  }

  document.querySelector("#tabla-usuarios").addEventListener("click", async (e) => {
    const boton = e.target.closest("button");
    if (!boton) return;
    const { activar, desactivar, restablecer, desbloquear, editar } = boton.dataset;
    if (editar) return empezarEdicion(Number(editar));
    const id = Number(activar || desactivar || restablecer || desbloquear);
    if (!id) return;
    if (restablecer && !confirm("Se generará una clave temporal y la clave actual dejará de servir. ¿Continuar?")) return;
    // El botón queda ocupado hasta que responda el backend (no se envía dos veces).
    await ocupado(boton, async () => {
      try {
        let r;
        if (activar) r = await servicio.activar(id);
        else if (desactivar) r = await servicio.desactivar(id);
        else if (desbloquear) r = await servicio.desbloquear(id);
        else r = await servicio.restablecerClave(id);
        // La clave temporal se muestra una sola vez (no se guarda en la pantalla ni en la auditoría).
        FerroSuite.aviso.mostrar(mensajes, r.datos && r.datos.claveTemporal ? [r.mensaje, "Clave temporal: " + r.datos.claveTemporal] : r.mensaje, "ok");
        await cargar();
      } catch (error) {
        FerroSuite.aviso.mostrarError(mensajes, error);
      }
    });
  });

  // Editar: nombre completo y rol. El nombre de usuario y la clave no se cambian aquí.
  function empezarEdicion(id) {
    const u = usuarios.find((x) => x.usuarioId === id);
    if (!u) return;
    editando = id;
    form.llenar({ nombreUsuario: u.nombreUsuario, nombreCompleto: u.nombreCompleto, rolCodigo: u.rolCodigo });
    form.form.elements.nombreUsuario.readOnly = true;
    form.form.elements.clave.required = false;
    form.form.elements.clave.parentElement.hidden = true;
    form.cambiarBoton("Guardar cambios");
    cancelar.hidden = false;
    document.querySelector("#titulo-form-usuario")?.replaceChildren("Editar usuario");
    document.querySelector("#ayuda-form-usuario")?.replaceChildren("Cambia el nombre completo o el rol. El nombre de usuario y la clave no se " +
      "modifican aquí (la clave se cambia con «Restablecer clave»). Un cambio de rol rige desde el próximo ingreso de ese usuario.");
    form.form.scrollIntoView({ behavior: "smooth" });
  }
  function terminarEdicion() {
    editando = null;
    form.form.reset();
    form.form.elements.nombreUsuario.readOnly = false;
    form.form.elements.clave.required = true;
    form.form.elements.clave.parentElement.hidden = false;
    form.cambiarBoton("Crear usuario");
    cancelar.hidden = true;
    document.querySelector("#titulo-form-usuario")?.replaceChildren("Crear usuario");
    document.querySelector("#ayuda-form-usuario")?.replaceChildren(ayudaCrear);
  }

  const form = FerroSuite.formulario.render(document.querySelector("#form-usuario"), {
    textoBoton: "Crear usuario",
    campos: [
      { nombre: "nombreUsuario", etiqueta: "Usuario", placeholder: "maria.gomez", requerido: true },
      { nombre: "nombreCompleto", etiqueta: "Nombre completo", requerido: true },
      { nombre: "rolCodigo", etiqueta: "Rol", tipo: "select", opciones: [] },
      { nombre: "clave", etiqueta: "Clave inicial", tipo: "password", requerido: true },
    ],
    validar(d) {
      const errores = [];
      if (!d.nombreUsuario) errores.push("Escribe el usuario.");
      if (!d.nombreCompleto) errores.push("Escribe el nombre completo.");
      if (!editando && (d.clave || "").length < 10) errores.push("La clave inicial debe tener al menos 10 caracteres.");
      return errores;
    },
    async alEnviar(d) {
      const r = editando
        ? await servicio.actualizar({ usuarioId: editando, nombreCompleto: d.nombreCompleto, rolCodigo: d.rolCodigo })
        : await servicio.crear(d);
      terminarEdicion();
      await cargar();
      return r.mensaje;
    },
  });
  const cancelar = document.createElement("button");
  cancelar.type = "button";
  cancelar.className = "btn btn-ghost";
  cancelar.textContent = "Cancelar edición";
  cancelar.hidden = true;
  cancelar.addEventListener("click", terminarEdicion);
  document.querySelector("#form-usuario").after(cancelar);

  await FerroSuite.sesionLista;
  if (!FerroSuite.puede("usuarios.gestionar")) return FerroSuite.sinAcceso();
  try {
    const roles = (await servicio.roles()).datos;
    form.actualizarOpciones("rolCodigo", roles.map((r) => ({ valor: r.codigo, texto: r.nombre })));
  } catch (error) {
    FerroSuite.mostrarErrorPagina(error);
  }
  cargar();
});
