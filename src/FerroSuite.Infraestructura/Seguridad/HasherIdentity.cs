using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FerroSuite.Infraestructura.Seguridad;

/// <summary>
/// Hash de claves con <see cref="PasswordHasher{TUser}"/> de ASP.NET Core Identity (formato V3: PBKDF2-HMAC-SHA512,
/// sal aleatoria de 128 bits, subclave de 256 bits). Se usan 210.000 iteraciones (recomendación de OWASP para
/// PBKDF2-HMAC-SHA512); un hash con menos iteraciones se marca para recalcularse al ingresar.
///
/// También verifica, una sola vez, las claves creadas por la versión 2 de FerroSuite (PBKDF2-SHA256 propio),
/// para que el programa las convierta al formato de Identity en el siguiente ingreso.
/// </summary>
public sealed class HasherIdentity : IHasherClaves
{
    public const int IteracionesPorDefecto = 210_000;

    /// <summary>Prefijo con el que el repositorio presenta un hash de la versión 2: "pbkdf2-sha256$iteraciones$sal$hash" (Base64).</summary>
    public const string PrefijoFormatoAnterior = "pbkdf2-sha256$";

    private static readonly Usuario SinUsuario = new(); // PasswordHasher no usa el usuario para calcular
    private readonly PasswordHasher<Usuario> hasher;

    public HasherIdentity(int iteraciones = IteracionesPorDefecto)
    {
        if (iteraciones < 10_000) throw new ArgumentOutOfRangeException(nameof(iteraciones));
        hasher = new PasswordHasher<Usuario>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = iteraciones,
        }));
    }

    public string Calcular(string clave) => hasher.HashPassword(SinUsuario, clave);

    public ResultadoClave Verificar(string hash, string clave)
    {
        if (string.IsNullOrEmpty(hash)) return ResultadoClave.Incorrecta;
        if (hash.StartsWith(PrefijoFormatoAnterior, StringComparison.Ordinal))
            return VerificarFormatoAnterior(hash, clave) ? ResultadoClave.CorrectaDebeActualizarse : ResultadoClave.Incorrecta;
        try
        {
            return hasher.VerifyHashedPassword(SinUsuario, hash, clave) switch
            {
                PasswordVerificationResult.Success => ResultadoClave.Correcta,
                PasswordVerificationResult.SuccessRehashNeeded => ResultadoClave.CorrectaDebeActualizarse,
                _ => ResultadoClave.Incorrecta,
            };
        }
        catch (FormatException)
        {
            return ResultadoClave.Incorrecta; // hash dañado: nunca da acceso
        }
    }

    public static string FormatoAnterior(byte[] hash, byte[] sal, int iteraciones) =>
        $"{PrefijoFormatoAnterior}{iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";

    private static bool VerificarFormatoAnterior(string texto, string clave)
    {
        var partes = texto[PrefijoFormatoAnterior.Length..].Split('$');
        if (partes.Length != 3 || !int.TryParse(partes[0], out var iteraciones) || iteraciones < 100_000) return false;
        try
        {
            var sal = Convert.FromBase64String(partes[1]);
            var esperado = Convert.FromBase64String(partes[2]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(clave, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException) { return false; }
    }
}
