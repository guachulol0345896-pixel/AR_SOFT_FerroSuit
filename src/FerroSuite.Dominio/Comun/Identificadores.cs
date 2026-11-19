using System.Text.RegularExpressions;

namespace FerroSuite.Dominio.Comun;

/// <summary>
/// Reglas de formato de los identificadores del negocio. Son las mismas que exigen las restricciones
/// CHECK de la base de datos (migración V002); aquí además se verifica el dígito de verificación del NIT.
/// </summary>
public static partial class Identificadores
{
    [GeneratedRegex(@"^[A-Z0-9](?:[A-Z0-9-]{1,18}[A-Z0-9])$")]
    private static partial Regex PatronSku();

    [GeneratedRegex(@"^(?<numero>\d{6,12})(?:-(?<dv>\d))?$")]
    private static partial Regex PatronNit();

    [GeneratedRegex(@"^[a-z0-9._-]{3,40}$")]
    private static partial Regex PatronUsuario();

    // Pesos de la DIAN para el dígito de verificación, desde el último dígito del número hacia la izquierda.
    private static readonly int[] PesosDian = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    public static string NormalizarSku(string? sku) => (sku ?? "").Trim().ToUpperInvariant();

    /// <summary>Devuelve el error para mostrar, o null si el SKU es válido (ya normalizado).</summary>
    public static string? ValidarSku(string sku)
    {
        if (sku.Length == 0) return "El SKU es obligatorio.";
        if (sku.Length < 3 || sku.Length > 20) return "El SKU debe tener entre 3 y 20 caracteres.";
        if (!PatronSku().IsMatch(sku)) return "El SKU solo puede tener letras sin tilde, números y guiones (no al inicio ni al final). Ej.: FER-0245.";
        return null;
    }

    public static string NormalizarNit(string? nit) => (nit ?? "").Trim().Replace(" ", "").Replace(".", "");

    /// <summary>Devuelve el error para mostrar, o null si el NIT es válido. Si trae dígito de verificación, debe ser el correcto.</summary>
    public static string? ValidarNit(string nit)
    {
        if (nit.Length == 0) return "El NIT es obligatorio.";
        var m = PatronNit().Match(nit);
        if (!m.Success) return "El NIT debe tener de 6 a 12 dígitos y, si aplica, el dígito de verificación (ej.: 900123456-8).";
        if (m.Groups["dv"].Success)
        {
            var esperado = DigitoVerificacion(m.Groups["numero"].Value);
            if (m.Groups["dv"].Value[0] - '0' != esperado)
                return $"El dígito de verificación del NIT no es correcto (para {m.Groups["numero"].Value} debería ser {esperado}).";
        }
        return null;
    }

    /// <summary>Dígito de verificación del NIT según el algoritmo de la DIAN (módulo 11).</summary>
    public static int DigitoVerificacion(string numero)
    {
        var suma = 0;
        for (var i = 0; i < numero.Length; i++)
            suma += (numero[numero.Length - 1 - i] - '0') * PesosDian[i];
        var residuo = suma % 11;
        return residuo < 2 ? residuo : 11 - residuo;
    }

    public static string NormalizarUsuario(string? usuario) => (usuario ?? "").Trim().ToLowerInvariant();

    public static string? ValidarNombreUsuario(string usuario) =>
        PatronUsuario().IsMatch(usuario) ? null
            : "El usuario debe tener de 3 a 40 caracteres: letras minúsculas sin tilde, números, punto, guion o guion bajo.";
}
