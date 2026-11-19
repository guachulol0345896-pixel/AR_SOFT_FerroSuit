// PRUEBAS DEL PRONÓSTICO DE DEMANDA (red neuronal ONNX integrada en C#). Datos SINTÉTICOS.
//  * Unitarias: preparación de entradas, festivos y serie de demanda contra casos de referencia exportados desde Python
//    (ml/exportar_casos_prueba.py); reglas de recomendación y respaldo con un modelo falso.
//  * Integración: el modelo ONNX REAL ejecutado con ONNX Runtime dentro del proceso, y el recorrido completo
//    Puente (WebView2) → aplicación → repositorio → preparación → ONNX → recomendación.
using System.Diagnostics;
using System.Text.Json.Nodes;
using FerroSuite.Infraestructura.IA.Prediccion;
using FerroSuite.Pruebas.Dobles;

namespace FerroSuite.Pruebas.Herramientas;

public static class PruebasPronostico
{
    /// <summary>Resultados medidos (se imprimen al final para documentarlos).</summary>
    public static readonly List<string> Mediciones = [];

    public static async Task Correr(Registro reg)
    {
        void Check(string id, string nombre, bool cond, string detalle = "") => reg.Check(id, nombre, cond, detalle);
        var rutaCasos = Path.Combine(Entorno.CarpetaModelos, "demanda_mlp_casos_prueba.json");
        if (!File.Exists(rutaCasos)) { Check("PF0", "Casos de referencia de Python disponibles", false, rutaCasos); return; }
        var casos = JsonNode.Parse(File.ReadAllText(rutaCasos))!;
        var festivosPython = casos["festivos"]!.AsArray().Select(f => DateOnly.Parse(f!.ToString())).ToHashSet();
        bool EsFestivoPython(DateOnly d) => festivosPython.Contains(d);

        // ================= Unitarias: equivalencia con el entrenamiento =================
        var nombresPython = casos["nombres_caracteristicas"]!.AsArray().Select(n => n!.ToString()).ToList();
        Check("PF1", "Las 48 entradas de C# tienen los mismos nombres y orden que en el entrenamiento",
            nombresPython.SequenceEqual(EspecificacionModelo.NombresCaracteristicas) && EspecificacionModelo.NumeroEntradas == 48);

        var calculados = Enumerable.Range(2023, 2).SelectMany(a => CalendarioFestivos.DelAnio(a)).ToHashSet();
        Check("PF2", "El calendario oficial de festivos de Colombia (C#) coincide con los festivos usados al entrenar (2023-2024)",
            calculados.SetEquals(festivosPython), $"{calculados.Count} festivos");
        var f2026 = new[] { "2026-01-01", "2026-01-12", "2026-03-23", "2026-04-02", "2026-04-03", "2026-05-01", "2026-05-18", "2026-06-08",
                            "2026-06-15", "2026-06-29", "2026-07-20", "2026-08-07", "2026-08-17", "2026-10-12", "2026-11-02", "2026-11-16",
                            "2026-12-08", "2026-12-25" }.Select(DateOnly.Parse).ToHashSet();
        Check("PF3", "Festivos de 2026 (Pascua 5 de abril; traslados al lunes) calculados correctamente",
            CalendarioFestivos.DelAnio(2026).SetEquals(f2026) && CalendarioFestivos.DomingoDePascua(2026) == new DateOnly(2026, 4, 5));

        double maxDif = 0; var invalidosOk = true; var validos = 0;
        foreach (var caso in casos["caracteristicas"]!.AsArray())
        {
            var ventana = caso!["ventana"]!.AsArray().Select(v => v is null ? double.NaN : v.GetValue<double>()).ToArray();
            var r = PreparadorCaracteristicas.Preparar(ventana, DateOnly.Parse(caso["fecha_origen"]!.ToString()), caso["categoria"]!.ToString(),
                caso["unidad"]!.ToString(), EsFestivoPython);
            if (!caso["valido"]!.GetValue<bool>()) { invalidosOk &= r is null; continue; }
            if (r is null) { maxDif = double.PositiveInfinity; continue; }
            validos++;
            var esperado = caso["caracteristicas"]!.AsArray().Select(v => v!.GetValue<double>()).ToArray();
            for (var i = 0; i < esperado.Length; i++) maxDif = Math.Max(maxDif, Math.Abs(esperado[i] - r.Caracteristicas[i]));
            maxDif = Math.Max(maxDif, Math.Abs(caso["escala"]!.GetValue<double>() - r.Escala));
        }
        Mediciones.Add($"Entradas C# vs Python: {validos} casos, diferencia máxima {maxDif:E2}");
        Check("PF4", $"Las entradas que prepara C# son iguales a las de Python en {validos} casos (incluye faltantes, ceros, decimales, festivos y Semana Santa)",
            validos >= 30 && maxDif <= 1e-5 && invalidosOk, $"diferencia máxima {maxDif:E2}");

        var seriesOk = true; var nSeries = 0;
        foreach (var s in casos["series"]!.AsArray())
        {
            var movs = s!["movimientos"]!.AsArray();
            var filas = movs.Select(m => new
                {
                    Dia = DateOnly.Parse(m!["Fecha"]!.ToString()), Sku = m["Sku"]!.ToString(), Tipo = m["Tipo"]!.ToString(),
                    Motivo = m["Motivo"]!.ToString(), Cantidad = m["Cantidad"]!.GetValue<decimal>(),
                })
                .GroupBy(m => (m.Sku, m.Dia))
                .Select(g => new VentaDiaria(g.Key.Sku, g.Key.Dia,
                    g.Where(m => m.Tipo == "Salida" && m.Motivo == "Venta").Sum(m => m.Cantidad),
                    g.Where(m => m.Tipo == "Entrada" && m.Motivo == "Devolución").Sum(m => m.Cantidad),
                    g.Sum(m => m.Tipo == "Entrada" ? m.Cantidad : -m.Cantidad))).ToList();
            var fechas = s["fechas"]!.AsArray().Select(f => DateOnly.Parse(f!.ToString())).ToList();
            var serie = SerieDemanda.Construir(filas, filas.Min(f => f.Dia), s["stock_actual"]!.GetValue<decimal>(), fechas[0], fechas[^1]);
            var esperado = s["demanda"]!.AsArray().Select(v => v is null ? double.NaN : v.GetValue<double>()).ToArray();
            seriesOk &= serie.Length == esperado.Length && serie.Zip(esperado).All(p => double.IsNaN(p.First) ? double.IsNaN(p.Second) : p.First == p.Second);
            nSeries++;
        }
        Check("PF5", $"La demanda diaria a partir de movimientos (ventas − devoluciones, agotados como faltantes, stock reconstruido hacia atrás) es igual a la de Python en {nSeries} casos",
            seriesOk && nSeries >= 4);

        // ================= Unitarias: recomendación y respaldo (modelo falso) =================
        var ahora = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.FromHours(-5));
        Entorno ConHistorial(IPronosticadorDemanda modelo, int dias = 40)
        {
            var e = Entorno.Nuevo(pronosticador: modelo, ahora: ahora);
            // SIN-002 (varilla, stock 10, mínimo 8, proveedor con 10 días): ventas diarias de 1 durante 'dias' días.
            for (var d = 1; d <= dias; d++)
                e.A.M.Add(new Movimiento { MovimientoId = 1000 + d, Sku = "SIN-002", Tipo = "Salida", Cantidad = 1, Motivo = "Venta", BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = ahora.DateTime.AddDays(-d) });
            for (var d = 1; d <= dias; d++)
                e.A.M.Add(new Movimiento { MovimientoId = 2000 + d, Sku = "SIN-001", Tipo = "Salida", Cantidad = 2.5m, Motivo = "Venta", BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = ahora.DateTime.AddDays(-d) });
            return e;
        }
        RecomendacionReposicion Fila(JsonNode r, string sku) =>
            System.Text.Json.JsonSerializer.Deserialize<RecomendacionReposicion>(r["datos"]!["recomendaciones"]!.AsArray().First(x => x!["sku"]!.ToString() == sku)!.ToJsonString(),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        {
            var falso = new PronosticadorFalso(valor: 2f);
            var e = ConHistorial(falso);
            await e.Ingresar("bodega.prueba");
            var r = await e.Pedir("pronosticos.reposicion");
            var v = Fila(r, "SIN-002");
            // stock 10 / 2 por día = 5 días; entrega 10 → se agota antes de que llegue: Urgente. Cantidad = 2·(10+15) + 8 − 10 = 48.
            Check("PU1", "Con el consumo del modelo: días hasta agotarse = stock / consumo, comparado con la entrega del proveedor, y cantidad sugerida",
                Entorno.Ok(r) && v.Fuente == PronosticoServicio.FuenteRed && v.ConsumoDiario == 2m && v.DiasHastaAgotarse == 5.0m && v.Urgencia == "Urgente"
                && v.CantidadSugerida == 48m && v.FechaEstimadaAgotamiento == DateOnly.FromDateTime(ahora.DateTime).AddDays(5) && v.PuedePrepararOrden,
                $"{v.Urgencia}, {v.DiasHastaAgotarse} días, pedir {v.CantidadSugerida}");
            Check("PU2", "El modelo recibe un lote con las entradas preparadas de cada producto con historia (48 valores y escala)",
                falso.Llamadas == 1 && falso.UltimasEntradas.Count == 2 && falso.UltimasEntradas.All(x => x.Caracteristicas.Length == 48 && x.Escala > 0));
            var datos = r["datos"]!;
            Check("PU3", "El horizonte es de hoy a hoy + 6 con datos hasta ayer", datos["fechaCorte"]!.ToString() == "2026-03-01"
                && datos["horizonteDesde"]!.ToString() == "2026-03-02" && datos["horizonteHasta"]!.ToString() == "2026-03-08");

            var ordenesAntes = e.A.O.Count;
            await e.Ingresar("admin.prueba");
            await e.Pedir("ordenes.crear", new { sku = "SIN-002", nitProveedor = "000000002-5", cantidad = 30 });
            await e.Ingresar("bodega.prueba");
            var v2 = Fila(await e.Pedir("pronosticos.reposicion"), "SIN-002");
            Check("PU4", "Descuenta lo que ya está pedido (orden pendiente) y el pronóstico nunca crea ni aprueba órdenes",
                v2.EnCamino == 30 && v2.CantidadSugerida == 18 && e.A.O.Count == ordenesAntes + 1 && e.A.O.All(o => o.Estado == EstadosOrden.Pendiente));
        }
        {
            var e = ConHistorial(new PronosticadorFalso(valor: 0.2f), dias: 12);
            await e.Ingresar("admin.prueba");
            var v = Fila(await e.Pedir("pronosticos.reposicion"), "SIN-002");
            // Solo 12 días de historia → regla de stock mínimo: consumo = vendido en 30 días / 30 = 12/30 = 0,4; stock 10 > mínimo 8,
            // 10/0,4 = 25 días > entrega 10 + 3 → sin acción.
            Check("PU5", "Sin historial suficiente se usa la regla de stock mínimo existente y se informa el motivo",
                v.EsRespaldo && v.Fuente == PronosticoServicio.FuenteRespaldo && (v.MotivoRespaldo ?? "").Contains("Historial insuficiente")
                && v.ConsumoDiario == 0.4m && v.Urgencia == "Sin acción", v.MotivoRespaldo ?? "");
            e.A.P.First(p => p.Sku == "SIN-002").StockActual = 7;
            var v2 = Fila(await e.Pedir("pronosticos.reposicion"), "SIN-002");
            Check("PU6", "Con el respaldo, si el stock está en el mínimo o por debajo se recomienda pedir (regla existente)",
                v2.EsRespaldo && v2.Urgencia == "Pedir ahora" && v2.CantidadSugerida > 0);
        }
        {
            var e = ConHistorial(new PronosticadorFalso(disponible: false));
            await e.Ingresar("admin.prueba");
            var r = await e.Pedir("pronosticos.reposicion");
            Check("PU7", "Si el modelo no está disponible, todo usa la regla de respaldo, se informa el motivo y no se llama al modelo",
                Entorno.Ok(r) && r["datos"]!["porRedNeuronal"]!.GetValue<int>() == 0 && !r["datos"]!["modelo"]!["disponible"]!.GetValue<bool>()
                && Fila(r, "SIN-002").MotivoRespaldo!.Contains("Modelo no disponible"));
        }
        {
            var falla = new PronosticadorFalso(falla: true);
            var e = ConHistorial(falla);
            await e.Ingresar("admin.prueba");
            var r = await e.Pedir("pronosticos.reposicion");
            var mov = await e.Pedir("movimientos.registrar", new { sku = "SIN-001", tipo = "Salida", cantidad = 1, motivo = "Venta", bodegaId = 1 });
            var prod = await e.Pedir("productos.crear", new { sku = "SIN-077", nombre = "Producto tras la falla", categoriaId = 1, unidad = "Unidad", stockActual = 1, stockMinimo = 0 });
            Check("PU8", "Si el modelo falla al calcular: respuesta con la regla de respaldo, falla auditada, y el inventario sigue funcionando",
                Entorno.Ok(r) && r["datos"]!["falloModelo"]!.GetValue<bool>() && r["datos"]!["porRespaldo"]!.GetValue<int>() >= 2
                && e.A.A.Any(x => x.Accion == "pronosticos.modelo" && x.Resultado == ResultadosAuditoria.Fallido) && Entorno.Ok(mov) && Entorno.Ok(prod));
        }

        // ================= Integración: ONNX Runtime real =================
        var real = Entorno.ModeloReal;
        Check("PI1", "El modelo ONNX real carga con ONNX Runtime (huella SHA-256, parámetros y grafo verificados)", real.Estado.Disponible,
            real.Estado.Disponible ? real.Estado.Descripcion : real.Estado.Motivo ?? "");
        if (!real.Estado.Disponible) return;

        var validosOnnx = casos["caracteristicas"]!.AsArray().Where(c => c!["valido"]!.GetValue<bool>()).ToList();
        var entradas = validosOnnx.Select(c => new EntradaModelo(c!["caracteristicas"]!.AsArray().Select(v => v!.GetValue<float>()).ToArray(), c["escala"]!.GetValue<float>())).ToList();
        var salida = real.Predecir(entradas);
        var difOnnx = salida.Select((v, i) => Math.Abs(v - validosOnnx[i]!["salida_onnx"]!.GetValue<double>())).Max();
        Mediciones.Add($"ONNX Runtime en C# vs ONNX Runtime en Python: {salida.Length} casos, diferencia máxima {difOnnx:E2} unidades/día");
        Check("PI2", $"ONNX Runtime en C# da la misma salida que en Python para {salida.Length} casos", difOnnx <= 1e-4, $"diferencia máxima {difOnnx:E2}");

        var sw = Stopwatch.StartNew();
        var lote = Enumerable.Range(0, 1000).Select(i => entradas[i % entradas.Count]).ToList();
        real.Predecir(lote);
        sw.Stop();
        Mediciones.Add($"Inferencia de 1000 productos en un lote: {sw.Elapsed.TotalMilliseconds:0.0} ms");

        // Recorrido completo con datos del periodo de prueba: SINT-001, ventas de los 30 días hasta el 2024-12-24.
        var rc = casos["recorrido"]!;
        var sku = rc["sku"]!.ToString();
        var fechasRc = rc["fechas"]!.AsArray().Select(f => DateOnly.Parse(f!.ToString())).ToList();
        var ventas = rc["ventas_30_dias"]!.AsArray().Select(v => v!.GetValue<decimal>()).ToList();
        var categoriaId = EspecificacionModelo.Categorias.ToList().IndexOf(rc["categoria"]!.ToString()) + 1;
        var er = Entorno.Nuevo(ahora: new DateTimeOffset(2024, 12, 25, 9, 0, 0, TimeSpan.FromHours(-5)));
        er.A.Sembrar(new Producto { Sku = sku, Nombre = "Producto sintético del recorrido", CategoriaId = categoriaId, Unidad = rc["unidad"]!.ToString(),
            StockActual = 40, StockMinimo = 10, PrecioCompra = 1000, NitProveedor = "000000002-5" });
        for (var i = 0; i < fechasRc.Count; i++)
            if (ventas[i] > 0)
                er.A.M.Add(new Movimiento { MovimientoId = 5000 + i, Sku = sku, Tipo = "Salida", Cantidad = ventas[i], Motivo = "Venta", BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = fechasRc[i].ToDateTime(new TimeOnly(11, 0)) });
        // El primer movimiento debe ser del primer día de la ventana aunque ese día no haya ventas.
        er.A.M.Add(new Movimiento { MovimientoId = 4999, Sku = sku, Tipo = "Entrada", Cantidad = 0.0001m, Motivo = "Ajuste por conteo", BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = fechasRc[0].ToDateTime(new TimeOnly(8, 0)) });
        await er.Ingresar("bodega.prueba");
        var respuesta = await er.Pedir("pronosticos.reposicion");
        var fila = Fila(respuesta, sku);
        var esperadoRc = (decimal)rc["salida_onnx"]!.GetValue<double>();
        Mediciones.Add($"Recorrido {sku}: red neuronal {fila.ConsumoDiario} por día (Python: {esperadoRc:0.####}); regla 30 días {fila.ConsumoRegla30Dias} (Python: {rc["regla_30_dias"]}); " +
                       $"stock 40 → {fila.DiasHastaAgotarse} días; entrega {fila.DiasEntrega}; {fila.Urgencia}; pedir {fila.CantidadSugerida}");
        Check("PI3", "Recorrido completo Puente → aplicación → historial → entradas → ONNX Runtime → recomendación: mismo consumo que Python para el mismo producto y fecha",
            Entorno.Ok(respuesta) && fila.Fuente == PronosticoServicio.FuenteRed && Math.Abs(fila.ConsumoDiario - Math.Round(esperadoRc, 2)) <= 0.01m
            && Math.Abs(fila.ConsumoRegla30Dias - (decimal)rc["regla_30_dias"]!.GetValue<double>()) <= 0.01m && fila.DiasHastaAgotarse is not null,
            $"C# {fila.ConsumoDiario} · Python {esperadoRc:0.####}");
        var orden = await er.Pedir("ordenes.crear", new { sku, nitProveedor = fila.NitProveedor, cantidad = fila.CantidadSugerida > 0 ? fila.CantidadSugerida : 1 });
        Check("PI4", "La recomendación sirve para preparar una orden, que queda pendiente de aprobación (nada se aprueba ni envía solo)",
            Entorno.Ok(orden) && er.A.O.Single().Estado == EstadosOrden.Pendiente && er.A.O.Single().FechaAprobacion is null);

        // Modelo dañado, ausente o de otra versión: no disponible, con motivo; el servicio responde con el respaldo.
        var tmp = Directory.CreateTempSubdirectory("ferrosuite-modelo-");
        try
        {
            foreach (var f in new[] { PronosticadorOnnx.ArchivoModelo, PronosticadorOnnx.ArchivoParametros })
                File.Copy(Path.Combine(Entorno.CarpetaModelos, f), Path.Combine(tmp.FullName, f));
            var bytes = File.ReadAllBytes(Path.Combine(tmp.FullName, PronosticadorOnnx.ArchivoModelo));
            bytes[^10] ^= 0xFF;
            File.WriteAllBytes(Path.Combine(tmp.FullName, PronosticadorOnnx.ArchivoModelo), bytes);
            using var danado = new PronosticadorOnnx(tmp.FullName);
            var json = File.ReadAllText(Path.Combine(Entorno.CarpetaModelos, PronosticadorOnnx.ArchivoParametros)).Replace("\"ventana_dias\": 28", "\"ventana_dias\": 21");
            File.Copy(Path.Combine(Entorno.CarpetaModelos, PronosticadorOnnx.ArchivoModelo), Path.Combine(tmp.FullName, PronosticadorOnnx.ArchivoModelo), true);
            File.WriteAllText(Path.Combine(tmp.FullName, PronosticadorOnnx.ArchivoParametros), json);
            using var otraVersion = new PronosticadorOnnx(tmp.FullName);
            using var ausente = new PronosticadorOnnx(Path.Combine(tmp.FullName, "no-existe"));
            var conDanado = ConHistorial(danado);
            await conDanado.Ingresar("admin.prueba");
            var rd = await conDanado.Pedir("pronosticos.reposicion");
            Check("PI5", "Modelo dañado, de otra versión o ausente: queda no disponible con un motivo claro y el pronóstico usa la regla de respaldo",
                !danado.Estado.Disponible && danado.Estado.Motivo!.Contains("huella") && !otraVersion.Estado.Disponible && otraVersion.Estado.Motivo!.Contains("versión")
                && !ausente.Estado.Disponible && ausente.Estado.Motivo!.Contains("No se encontró")
                && Entorno.Ok(rd) && rd["datos"]!["porRedNeuronal"]!.GetValue<int>() == 0,
                $"{danado.Estado.Motivo} | {otraVersion.Estado.Motivo} | {ausente.Estado.Motivo}");
        }
        finally { tmp.Delete(true); }

        {
            var e = ConHistorial(Entorno.ModeloReal);
            await e.Ingresar("vendedor.prueba");
            var v = await e.Pedir("pronosticos.reposicion");
            await e.Ingresar("contador.prueba");
            var c = await e.Pedir("pronosticos.reposicion");
            Check("PI6", "El vendedor y el contador no pueden consultar el pronóstico (permiso de administrador y bodega)", Entorno.Denegado(v) && Entorno.Denegado(c));
        }

        // ================= Pantalla de Inteligencia Artificial: detalle por producto (pronosticos.producto) =================
        await PruebasDetalle(reg, er, sku, rc, fechasRc, ventas, esperadoRc, ConHistorial, ahora);
    }

    static async Task PruebasDetalle(Registro reg, Entorno er, string sku, JsonNode rc, List<DateOnly> fechasRc, List<decimal> ventas, decimal esperadoRc,
        Func<IPronosticadorDemanda, int, Entorno> conHistorial, DateTimeOffset ahora)
    {
        void Check(string id, string nombre, bool cond, string detalle = "") => reg.Check(id, nombre, cond, detalle);
        static bool Igual(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

        // PD1-PD3: producto CON datos suficientes (recorrido de referencia de Python), con el modelo ONNX real.
        await er.Ingresar("bodega.prueba");
        var lista = await er.Pedir("pronosticos.reposicion");
        var det = await er.Pedir("pronosticos.producto", new { sku });
        var d = det["datos"]!;
        var r = d["recomendacion"]!;
        var filaLista = lista["datos"]!["recomendaciones"]!.AsArray().First(x => x!["sku"]!.ToString() == sku);
        Check("PD1", "Detalle de un producto con historial: usa la red neuronal, da el mismo consumo que Python y la MISMA recomendación que la lista",
            Entorno.Ok(det) && r["fuente"]!.ToString() == PronosticoServicio.FuenteRed && !r["esRespaldo"]!.GetValue<bool>()
            && Math.Abs(r["consumoDiario"]!.GetValue<decimal>() - Math.Round(esperadoRc, 2)) <= 0.01m && Igual(r, filaLista),
            $"detalle {r["consumoDiario"]} · Python {esperadoRc:0.####}");

        var hist = d["historial"]!.AsArray();
        var corte = DateOnly.Parse(d["fechaCorte"]!.ToString());
        var ult30 = hist.TakeLast(30).ToList();
        var vendido30 = ult30.Sum(h => h!["demanda"]?.GetValue<decimal>() ?? 0);
        var ventana = hist.Where(h => h!["enVentanaModelo"]!.GetValue<bool>()).ToList();
        var fechasOk = ult30.Select(h => DateOnly.Parse(h!["fecha"]!.ToString())).SequenceEqual(fechasRc);
        Check("PD2", "El historial que se muestra son los datos reales del producto: 90 días hasta ayer, ventas iguales a los movimientos, ventana de 28 días marcada, días previos sin historial",
            hist.Count == 90 && corte == new DateOnly(2024, 12, 24) && DateOnly.Parse(hist[^1]!["fecha"]!.ToString()) == corte && fechasOk
            && Math.Abs(vendido30 - ventas.Sum()) < 0.01m && ventana.Count == 28 && ventana.All(h => DateOnly.Parse(h!["fecha"]!.ToString()) > corte.AddDays(-28))
            && hist.Take(60).All(h => h!["estado"]!.ToString() == "sin_historia" && h["demanda"] is null)
            && d["resumen"]!["vendidoUltimos30Dias"]!.GetValue<decimal>() == Math.Round(ventas.Sum(), 2) && d["resumen"]!["suficienteParaModelo"]!.GetValue<bool>(),
            $"{hist.Count} días; vendido 30 días {vendido30} vs movimientos {ventas.Sum()}");

        var pron = d["pronostico"]!.AsArray();
        var c = r["consumoDiario"]!.GetValue<decimal>();
        var stock = r["stockActual"]!.GetValue<decimal>();
        Check("PD3", "Pronóstico de 7 días desde hoy con el consumo del modelo; stock proyectado y demanda total del horizonte coherentes",
            pron.Count == 7 && DateOnly.Parse(pron[0]!["fecha"]!.ToString()) == new DateOnly(2024, 12, 25)
            && pron.Select((p, k) => p!["consumoEstimado"]!.GetValue<decimal>() == c && p["stockProyectado"]!.GetValue<decimal>() == Math.Max(0, Math.Round(stock - c * (k + 1), 2))).All(x => x)
            && d["demandaEstimadaHorizonte"]!.GetValue<decimal>() == Math.Round(c * 7, 2)
            && r["riesgoDesabastecimiento"]!.ToString().Length > 0 && r["riesgoDescripcion"]!.ToString().Length > 0);

        // PD4: producto SIN historial -> regla de respaldo, motivo claro, historial vacío (todo "sin_historia").
        er.A.Sembrar(new Producto { Sku = "SIN-NUEVO", Nombre = "Producto sintético sin movimientos", CategoriaId = 2, Unidad = "Unidad", StockActual = 0, StockMinimo = 5, PrecioCompra = 100, NitProveedor = "000000001-8" });
        var sn = await er.Pedir("pronosticos.producto", new { sku = "sin-nuevo" });
        var rn = sn["datos"]?["recomendacion"];
        Check("PD4", "Detalle de un producto sin historial: regla de stock mínimo, motivo informado, días hasta agotarse no calculado y riesgo alto por stock en cero",
            Entorno.Ok(sn) && rn!["esRespaldo"]!.GetValue<bool>() && rn["fuente"]!.ToString() == PronosticoServicio.FuenteRespaldo
            && rn["motivoRespaldo"]!.ToString().Contains("no tiene movimientos") && rn["diasHastaAgotarse"] is null
            && sn["datos"]!["historial"]!.AsArray().All(h => h!["estado"]!.ToString() == "sin_historia")
            && !sn["datos"]!["resumen"]!["suficienteParaModelo"]!.GetValue<bool>() && sn["datos"]!["resumen"]!["primerMovimiento"] is null
            && rn["riesgoDesabastecimiento"]!.ToString() == "Alto" && rn["cantidadSugerida"]!.GetValue<decimal>() > 0,
            rn?.ToJsonString() ?? sn.ToJsonString());

        // PD5: SKU desconocido, vacío o inactivo -> error de validación (no un fallo del programa).
        var desconocido = await er.Pedir("pronosticos.producto", new { sku = "NO-EXISTE" });
        var vacio = await er.Pedir("pronosticos.producto", new { sku = "" });
        er.A.P.First(p => p.Sku == "SIN-NUEVO").Activo = false;
        var inactivo = await er.Pedir("pronosticos.producto", new { sku = "SIN-NUEVO" });
        Check("PD5", "SKU inexistente, vacío o inactivo: mensaje de validación claro",
            !Entorno.Ok(desconocido) && Entorno.Errores(desconocido).Contains("no existe") && !Entorno.Ok(vacio) && !Entorno.Ok(inactivo)
            && !Entorno.Denegado(desconocido), Entorno.Errores(desconocido));

        // PD6: el modelo falla al calcular -> detalle con la regla de respaldo y el inventario sigue funcionando.
        var ef = conHistorial(new PronosticadorFalso(falla: true), 40);
        await ef.Ingresar("bodega.prueba");
        var df = await ef.Pedir("pronosticos.producto", new { sku = "SIN-002" });
        var inv = await ef.Pedir("productos.listar");
        Check("PD6", "Si el modelo falla, el detalle usa la regla de respaldo, lo indica y el inventario sigue respondiendo",
            Entorno.Ok(df) && df["datos"]!["falloModelo"]!.GetValue<bool>() && df["datos"]!["recomendacion"]!["esRespaldo"]!.GetValue<bool>()
            && df["datos"]!["recomendacion"]!["motivoRespaldo"]!.ToString().Contains("falló") && df["datos"]!["resumen"]!["suficienteParaModelo"]!.GetValue<bool>()
            && Entorno.Ok(inv));

        // PD7: modelo no disponible -> respaldo con el motivo del modelo; el historial sí era suficiente.
        var en = conHistorial(new PronosticadorFalso(disponible: false), 40);
        await en.Ingresar("admin.prueba");
        var dn = await en.Pedir("pronosticos.producto", new { sku = "SIN-001" });
        Check("PD7", "Modelo no disponible: el detalle lo informa (estado del modelo y motivo) y usa la regla de respaldo",
            Entorno.Ok(dn) && !dn["datos"]!["modelo"]!["disponible"]!.GetValue<bool>()
            && dn["datos"]!["recomendacion"]!["motivoRespaldo"]!.ToString().StartsWith("Modelo no disponible")
            && Math.Abs(dn["datos"]!["recomendacion"]!["consumoDiario"]!.GetValue<decimal>() - 2.5m) < 0.01m);

        // PD8: días agotado = demanda no observada (null), no cuenta como cero.
        var ea = Entorno.Nuevo(pronosticador: Entorno.ModeloReal, ahora: ahora);
        // SIN-002 (stock actual 10): 20 días con 1 venta diaria, antes 5 días agotado (compra de 30 al terminar), antes 20 días vendiendo 2.
        for (var k = 1; k <= 20; k++) ea.A.M.Add(new Movimiento { MovimientoId = 7000 + k, Sku = "SIN-002", Tipo = "Salida", Cantidad = 1, Motivo = "Venta", BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = ahora.DateTime.AddDays(-k) });
        ea.A.M.Add(new Movimiento { MovimientoId = 7100, Sku = "SIN-002", Tipo = "Entrada", Cantidad = 30, Motivo = "Compra", BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = ahora.DateTime.AddDays(-21) });
        for (var k = 27; k <= 46; k++) ea.A.M.Add(new Movimiento { MovimientoId = 7200 + k, Sku = "SIN-002", Tipo = "Salida", Cantidad = 2, Motivo = "Venta", BodegaId = 1, UsuarioId = 1, Usuario = "SINTÉTICO", Fecha = ahora.DateTime.AddDays(-k) });
        await ea.Ingresar("admin.prueba");
        var da = (await ea.Pedir("pronosticos.producto", new { sku = "SIN-002" }))["datos"]!;
        var agotados = da["historial"]!.AsArray().Where(h => h!["estado"]!.ToString() == "agotado").ToList();
        Check("PD8", "Los días con stock en cero sin ventas se muestran como agotado (demanda no observada), no como venta cero",
            agotados.Count >= 5 && agotados.All(h => h!["demanda"] is null) && da["resumen"]!["diasAgotado"]!.GetValue<int>() == agotados.Count,
            $"{agotados.Count} días agotado");

        // PD9: el riesgo corresponde a la urgencia en todos los productos.
        var mapa = new Dictionary<string, string> { ["Urgente"] = "Alto", ["Pedir ahora"] = "Medio", ["Programar pedido"] = "Bajo", ["Sin acción"] = "Muy bajo" };
        var filas = lista["datos"]!["recomendaciones"]!.AsArray().Concat((await ef.Pedir("pronosticos.reposicion"))["datos"]!["recomendaciones"]!.AsArray()).ToList();
        Check("PD9", "Nivel de riesgo de desabastecimiento coherente con la urgencia (Alto, Medio, Bajo, Muy bajo)",
            filas.Count > 0 && filas.All(f => mapa[f!["urgencia"]!.ToString()] == f["riesgoDesabastecimiento"]!.ToString()));

        // PD10: permisos.
        await er.Ingresar("vendedor.prueba");
        var pv = await er.Pedir("pronosticos.producto", new { sku });
        await er.Ingresar("contador.prueba");
        var pc = await er.Pedir("pronosticos.producto", new { sku });
        await er.Ingresar("admin.prueba");
        var pa = await er.Pedir("pronosticos.producto", new { sku });
        Check("PD10", "Detalle por producto: el administrador y bodega pueden consultarlo; vendedor y contador no", Entorno.Denegado(pv) && Entorno.Denegado(pc) && Entorno.Ok(pa));
    }
}
