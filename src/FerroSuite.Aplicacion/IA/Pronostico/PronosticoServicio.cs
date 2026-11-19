using System.Diagnostics;

namespace FerroSuite.Aplicacion.IA.Pronostico;

/// <summary>Recomendación de reposición de un producto. Es solo una sugerencia: nunca crea, aprueba ni envía órdenes.</summary>
public sealed record RecomendacionReposicion
{
    public required string Sku { get; init; }
    public required string Nombre { get; init; }
    public string? Categoria { get; init; }
    public required string Unidad { get; init; }
    public decimal StockActual { get; init; }
    public decimal StockMinimo { get; init; }
    /// <summary>Consumo diario estimado para los próximos 7 días (unidades del producto por día).</summary>
    public decimal ConsumoDiario { get; init; }
    /// <summary>"Red neuronal" o "Regla de stock mínimo" (respaldo).</summary>
    public required string Fuente { get; init; }
    public bool EsRespaldo { get; init; }
    /// <summary>Por qué se usó el respaldo (historial insuficiente, modelo no disponible o con error).</summary>
    public string? MotivoRespaldo { get; init; }
    /// <summary>Consumo con la regla actual (vendido en 30 días / 30), como referencia.</summary>
    public decimal ConsumoRegla30Dias { get; init; }
    public decimal? DiasHastaAgotarse { get; init; }
    public DateOnly? FechaEstimadaAgotamiento { get; init; }
    public string? Proveedor { get; init; }
    public string? NitProveedor { get; init; }
    public int DiasEntrega { get; init; }
    public bool DiasEntregaPorDefecto { get; init; }
    /// <summary>Cantidad ya pedida en órdenes pendientes o aprobadas que aún no se han recibido.</summary>
    public decimal EnCamino { get; init; }
    /// <summary>"Urgente", "Pedir ahora", "Programar pedido" o "Sin acción".</summary>
    public required string Urgencia { get; init; }
    /// <summary>Nivel de riesgo de desabastecimiento: "Alto", "Medio", "Bajo" o "Muy bajo".</summary>
    public string RiesgoDesabastecimiento { get; init; } = "";
    public string RiesgoDescripcion { get; init; } = "";
    public decimal CantidadSugerida { get; init; }
    public required string Explicacion { get; init; }
    /// <summary>true si se puede usar para preparar una orden (hay cantidad y proveedor activo).</summary>
    public bool PuedePrepararOrden { get; init; }
}

/// <summary>Un día del historial: demanda (null si no se observó) y por qué.</summary>
/// <param name="Estado">"venta", "sin_venta", "agotado" (stock en cero sin ventas: demanda no observada) o "sin_historia".</param>
public sealed record DiaDemanda(DateOnly Fecha, decimal? Demanda, string Estado, bool EnVentanaModelo);

/// <summary>Un día del horizonte: consumo estimado (promedio diario del modelo o de la regla) y stock proyectado al cierre.</summary>
public sealed record DiaPronostico(DateOnly Fecha, decimal ConsumoEstimado, decimal StockProyectado);

public sealed record ResumenHistorial(DateOnly? PrimerMovimiento, int DiasDeHistoria, int DiasConDatoEnVentana, int DiasAgotado,
    int DiasConVenta, decimal VendidoUltimos30Dias, bool SuficienteParaModelo);

/// <summary>Detalle de un producto para la pantalla de Inteligencia Artificial. Todo lo calcula el backend.</summary>
public sealed record DetallePronostico(
    DateOnly FechaCorte, DateOnly HorizonteDesde, DateOnly HorizonteHasta, EstadoModelo Modelo, bool FalloModelo,
    RecomendacionReposicion Recomendacion, IReadOnlyList<DiaDemanda> Historial, DateOnly VentanaModeloDesde, DateOnly VentanaModeloHasta,
    IReadOnlyList<DiaPronostico> Pronostico, decimal DemandaEstimadaHorizonte, ResumenHistorial Resumen);

public sealed record ResultadoPronostico(
    DateOnly FechaCorte, DateOnly HorizonteDesde, DateOnly HorizonteHasta, EstadoModelo Modelo, bool FalloModelo,
    int PorRedNeuronal, int PorRespaldo, IReadOnlyList<RecomendacionReposicion> Recomendaciones);

/// <summary>
/// Pronóstico de demanda y recomendación de reposición:
///  1. lee el historial diario de ventas de SQL Server (repositorio);
///  2. prepara las entradas igual que en el entrenamiento (SerieDemanda + PreparadorCaracteristicas);
///  3. el modelo neuronal (ONNX Runtime, local) estima el consumo diario de los próximos 7 días;
///  4. calcula cuándo se agotaría el stock actual, lo compara con el tiempo de entrega del proveedor y descuenta lo
///     que ya está pedido;
///  5. devuelve recomendaciones. No crea, no aprueba ni envía órdenes.
/// Si no hay historial suficiente o el modelo no está disponible o falla, usa la regla de stock mínimo existente
/// (CalculadoraReposicion) e informa el motivo. Un error del modelo nunca impide las operaciones de inventario.
/// </summary>
public sealed class PronosticoServicio(IRepositorioInventario inventario, IRepositorioCompras compras, IPronosticadorDemanda modelo,
    Autorizador autorizador, TimeProvider reloj)
{
    public const string FuenteRed = "Red neuronal";
    public const string FuenteRespaldo = "Regla de stock mínimo";

    public ResultadoPronostico RecomendarReposicion(SesionUsuario? sesion)
    {
        autorizador.Exigir(sesion, Permisos.VerPronosticos, "pronosticos.reposicion");
        var c = Calcular(sesion, inventario.ListarProductos(), EspecificacionModelo.HistoriaMinima);
        var lista = c.Productos.Select(x => x.Recomendacion)
            .OrderBy(r => r.Urgencia switch { "Urgente" => 0, "Pedir ahora" => 1, "Programar pedido" => 2, _ => 3 })
            .ThenBy(r => r.DiasHastaAgotarse ?? decimal.MaxValue)
            .ToList();
        return new ResultadoPronostico(c.Corte, c.Hoy, c.Hoy.AddDays(EspecificacionModelo.Horizonte - 1), c.Estado, c.Fallo,
            lista.Count(r => !r.EsRespaldo), lista.Count(r => r.EsRespaldo), lista);
    }

    /// <summary>
    /// Pronóstico de UN producto para la pantalla de Inteligencia Artificial: además de la recomendación (idéntica a la
    /// de la lista), devuelve el historial diario que se usó, la ventana que vio el modelo y la proyección de los 7 días.
    /// </summary>
    public DetallePronostico PronosticarProducto(SesionUsuario? sesion, string sku, int diasHistorial = 90)
    {
        autorizador.Exigir(sesion, Permisos.VerPronosticos, "pronosticos.producto");
        sku = Identificadores.NormalizarSku(sku ?? "");
        if (sku.Length == 0) throw new ErrorValidacion(["Elige un producto."]);
        diasHistorial = Math.Clamp(diasHistorial, EspecificacionModelo.HistoriaMinima, 365);
        var producto = inventario.BuscarProducto(sku);
        if (producto is null || !producto.Activo) throw new ErrorValidacion([$"El producto {sku} no existe o está inactivo."]);

        var c = Calcular(sesion, [producto], diasHistorial);
        var x = c.Productos.Single();
        var r = x.Recomendacion;
        var desde = c.Corte.AddDays(-(diasHistorial - 1));
        var inicioVentana = c.Corte.AddDays(-(EspecificacionModelo.Ventana - 1));
        var historial = x.Serie.Select((v, i) =>
        {
            var dia = desde.AddDays(i);
            var estado = x.PrimerMovimiento is not DateOnly pm || dia < pm ? "sin_historia"
                : double.IsNaN(v) ? "agotado" : v == 0 ? "sin_venta" : "venta";
            return new DiaDemanda(dia, double.IsNaN(v) ? null : Math.Round((decimal)v, 2), estado, dia >= inicioVentana);
        }).ToList();
        var ultimos30 = historial.TakeLast(EspecificacionModelo.HistoriaMinima).ToList();
        var ventana = historial.Where(h => h.EnVentanaModelo).ToList();
        var pronostico = Enumerable.Range(0, EspecificacionModelo.Horizonte).Select(k =>
            new DiaPronostico(c.Hoy.AddDays(k), r.ConsumoDiario, Math.Max(0, Math.Round(r.StockActual - r.ConsumoDiario * (k + 1), 2)))).ToList();
        var resumen = new ResumenHistorial(
            PrimerMovimiento: x.PrimerMovimiento,
            DiasDeHistoria: x.PrimerMovimiento is DateOnly p0 ? Math.Max(0, c.Corte.DayNumber - p0.DayNumber + 1) : 0,
            DiasConDatoEnVentana: ventana.Count(h => h.Demanda is not null),
            DiasAgotado: historial.Count(h => h.Estado == "agotado"),
            DiasConVenta: historial.Count(h => h.Estado == "venta"),
            VendidoUltimos30Dias: ultimos30.Sum(h => h.Demanda ?? 0),
            SuficienteParaModelo: x.HistorialSuficiente);
        return new DetallePronostico(c.Corte, c.Hoy, c.Hoy.AddDays(EspecificacionModelo.Horizonte - 1), c.Estado, c.Fallo,
            r, historial, inicioVentana, c.Corte, pronostico, Math.Round(r.ConsumoDiario * EspecificacionModelo.Horizonte, 2), resumen);
    }

    private sealed record Calculado(Producto Producto, double[] Serie, DateOnly? PrimerMovimiento, bool HistorialSuficiente, RecomendacionReposicion Recomendacion);
    private sealed record Calculo(DateOnly Hoy, DateOnly Corte, EstadoModelo Estado, bool Fallo, List<Calculado> Productos);

    // Cálculo común de la lista y del detalle: así los dos dan exactamente la misma recomendación.
    private Calculo Calcular(SesionUsuario? sesion, IReadOnlyList<Producto> productos, int diasSerie)
    {
        // Día de corte = ayer (último día completo). Se pronostica de hoy a hoy + 6.
        var hoy = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
        var corte = hoy.AddDays(-1);
        var desde30 = corte.AddDays(-(EspecificacionModelo.HistoriaMinima - 1));
        var desdeSerie = corte.AddDays(-(diasSerie - 1));
        var historial = inventario.ObtenerHistorialVentas(desdeSerie);
        var porSku = historial.Filas.GroupBy(f => f.Sku).ToDictionary(g => g.Key, g => g.ToList());
        var enCamino = PedidosEnCamino();

        // 1-2. Series y entradas.
        var estado = modelo.Estado;
        var preparados = new List<(Producto P, double[] Serie, DateOnly? Primero, EntradaModelo? Entrada, string? Motivo, bool Suficiente)>();
        foreach (var p in productos)
        {
            DateOnly? primero = historial.PrimerMovimiento.TryGetValue(p.Sku, out var pm) ? pm : null;
            var serie = SerieDemanda.Construir(porSku.GetValueOrDefault(p.Sku) ?? [], primero, p.StockActual, desdeSerie, corte);
            string? motivo = null;
            EntradaModelo? entrada = null;
            if (primero is not DateOnly pr || pr > desde30)
                motivo = primero is DateOnly pr2
                    ? $"Historial insuficiente: el primer movimiento es del {pr2:yyyy-MM-dd}; el modelo necesita {EspecificacionModelo.HistoriaMinima} días."
                    : "Historial insuficiente: el producto no tiene movimientos.";
            else
            {
                entrada = PreparadorCaracteristicas.Preparar(serie.AsSpan(serie.Length - EspecificacionModelo.Ventana), corte,
                    p.Categoria, p.Unidad, CalendarioFestivos.EsFestivo);
                if (entrada is null)
                {
                    var conDato = serie[^EspecificacionModelo.Ventana..].Count(v => !double.IsNaN(v));
                    motivo = $"Historial insuficiente: solo {conDato} de los últimos {EspecificacionModelo.Ventana} días tienen ventas registradas " +
                             $"(los demás estuvo agotado o sin registro); el modelo necesita {EspecificacionModelo.MinDiasVentana}.";
                }
            }
            var suficiente = entrada is not null;
            if (entrada is not null && !estado.Disponible) { motivo = "Modelo no disponible: " + estado.Motivo; entrada = null; }
            preparados.Add((p, serie, primero, entrada, motivo, suficiente));
        }

        // 3. Modelo neuronal (un solo lote). Si falla, todo pasa al respaldo y el programa sigue funcionando.
        var conEntrada = preparados.Where(x => x.Entrada is not null).ToList();
        var consumos = new Dictionary<string, decimal>();
        var fallo = false;
        if (conEntrada.Count > 0)
        {
            try
            {
                var salida = modelo.Predecir(conEntrada.Select(x => x.Entrada!).ToList());
                if (salida.Length != conEntrada.Count) throw new InvalidOperationException("El modelo devolvió una cantidad de resultados distinta.");
                for (var i = 0; i < salida.Length; i++)
                    if (float.IsFinite(salida[i]) && salida[i] >= 0) consumos[conEntrada[i].P.Sku] = Math.Round((decimal)salida[i], 3);
            }
            catch (Exception ex)
            {
                fallo = true;
                Trace.WriteLine($"El modelo de demanda falló ({ex.GetType().Name}); se usa la regla de stock mínimo.");
                autorizador.Auditoria.Registrar(sesion, "pronosticos.modelo", ResultadosAuditoria.Fallido, detalle: "El modelo falló: " + ex.GetType().Name);
            }
        }

        // 4-5. Recomendaciones (la regla de 30 días usa siempre los últimos 30 días de la serie).
        var lista = preparados.Select(x =>
        {
            var ultimos30 = x.Serie[^EspecificacionModelo.HistoriaMinima..];
            var regla30 = Math.Round((decimal)ultimos30.Where(v => !double.IsNaN(v)).Sum() / EspecificacionModelo.HistoriaMinima, 3);
            RecomendacionReposicion r;
            if (consumos.TryGetValue(x.P.Sku, out var consumo))
                r = Recomendar(x.P, consumo, regla30, FuenteRed, null, enCamino, hoy);
            else
            {
                var motivo = x.Motivo ?? (fallo ? "El modelo falló al calcular; se usó la regla de stock mínimo." : "El modelo no devolvió un valor válido.");
                r = Recomendar(x.P, regla30, regla30, FuenteRespaldo, motivo, enCamino, hoy);
            }
            return new Calculado(x.P, x.Serie, x.Primero, x.Suficiente, r);
        }).ToList();
        return new Calculo(hoy, corte, estado, fallo, lista);
    }

    private RecomendacionReposicion Recomendar(Producto p, decimal consumo, decimal regla30, string fuente, string? motivoRespaldo,
        IReadOnlyDictionary<string, decimal> enCamino, DateOnly hoy)
    {
        var porDefecto = p.DiasEntregaProveedor is null;
        var entrega = p.DiasEntregaProveedor ?? CalculadoraReposicion.DiasEntregaPorDefecto;
        decimal? dias = consumo > 0 ? Math.Round(Math.Max(p.StockActual, 0) / consumo, 1) : null;
        var pedido = enCamino.GetValueOrDefault(p.Sku);
        var esRespaldo = fuente == FuenteRespaldo;

        // Comparación con el tiempo de entrega. Con el respaldo manda la regla existente: stock mínimo o se acaba antes de entrega + 3 días.
        string urgencia;
        if (p.StockActual <= 0 || dias is decimal d0 && d0 <= entrega) urgencia = "Urgente";
        else if (p.StockActual <= p.StockMinimo || dias is decimal d1 && d1 <= entrega + CalculadoraReposicion.MargenDias) urgencia = "Pedir ahora";
        else if (!esRespaldo && dias is decimal d2 && d2 <= entrega + 7) urgencia = "Programar pedido";
        else urgencia = "Sin acción";

        // Nivel de riesgo de desabastecimiento (lo que ve el encargado de bodega).
        var (riesgo, riesgoTexto) = urgencia switch
        {
            "Urgente" => ("Alto", p.StockActual <= 0 ? "Ya no hay stock." : "Se agotaría antes de que llegue un pedido hecho hoy."),
            "Pedir ahora" => ("Medio", p.StockActual <= p.StockMinimo ? "Está en el stock mínimo o por debajo." : $"Se agotaría poco después de la entrega (margen de {CalculadoraReposicion.MargenDias} días o menos)."),
            "Programar pedido" => ("Bajo", "Alcanza para la entrega, pero hay que pedir esta semana."),
            _ => ("Muy bajo", dias is null ? "No se espera consumo en los próximos días." : "El stock alcanza para el tiempo de entrega y está sobre el mínimo."),
        };

        var cantidad = 0m;
        if (urgencia != "Sin acción")
        {
            cantidad = CalculadoraReposicion.CantidadSugerida(consumo, entrega, p.StockMinimo, p.StockActual) - pedido;
            cantidad = cantidad <= 0 ? 0 : ReglasInventario.AdmiteDecimales(p.Unidad) ? Math.Ceiling(cantidad * 10) / 10 : Math.Ceiling(cantidad);
        }

        var u = p.Unidad.ToLowerInvariant();
        var partes = new List<string>
        {
            esRespaldo
                ? $"Consumo con la regla actual: {consumo:0.##} {u}/día (vendido en 30 días / 30)."
                : $"La red neuronal estima {consumo:0.##} {u}/día para los próximos 7 días (regla actual: {regla30:0.##}).",
            dias is decimal dd ? $"Con {p.StockActual:0.##} en stock alcanza para unos {dd:0.#} días." : "Sin consumo esperado: el stock no se agota en el horizonte.",
            $"El proveedor tarda {entrega} día(s){(porDefecto ? " (valor por defecto: sin proveedor asignado)" : "")}.",
        };
        if (p.StockActual <= p.StockMinimo) partes.Add($"Está en el stock mínimo ({p.StockMinimo:0.##}) o por debajo.");
        if (pedido > 0) partes.Add($"Ya hay {pedido:0.##} pedido(s) en órdenes sin recibir.");
        if (urgencia != "Sin acción" && cantidad == 0) partes.Add("Lo ya pedido cubre la necesidad: no hace falta otra orden.");

        return new RecomendacionReposicion
        {
            Sku = p.Sku, Nombre = p.Nombre, Categoria = p.Categoria, Unidad = p.Unidad, StockActual = p.StockActual, StockMinimo = p.StockMinimo,
            ConsumoDiario = Math.Round(consumo, 2), Fuente = fuente, EsRespaldo = esRespaldo, MotivoRespaldo = motivoRespaldo,
            ConsumoRegla30Dias = Math.Round(regla30, 2), DiasHastaAgotarse = dias,
            FechaEstimadaAgotamiento = dias is decimal df && df < 3650 ? hoy.AddDays((int)Math.Floor(df)) : null,
            Proveedor = p.NombreProveedor, NitProveedor = p.NitProveedor, DiasEntrega = entrega, DiasEntregaPorDefecto = porDefecto,
            EnCamino = pedido, Urgencia = urgencia, CantidadSugerida = cantidad, Explicacion = string.Join(" ", partes),
            RiesgoDesabastecimiento = riesgo, RiesgoDescripcion = riesgoTexto,
            PuedePrepararOrden = cantidad > 0 && p.NitProveedor is not null,
        };
    }

    // Cantidades en órdenes que todavía no han llegado (pendientes de aprobación o aprobadas sin recibir).
    private Dictionary<string, decimal> PedidosEnCamino() =>
        compras.ListarOrdenes()
            .Where(o => o.Estado is EstadosOrden.Pendiente or EstadosOrden.Aprobada)
            .SelectMany(o => o.Detalles)
            .GroupBy(d => d.Sku)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Cantidad));
}
