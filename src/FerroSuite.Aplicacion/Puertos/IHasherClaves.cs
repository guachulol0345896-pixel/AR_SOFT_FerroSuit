namespace FerroSuite.Aplicacion.Puertos;

public enum ResultadoClave
{
    Incorrecta,
    Correcta,
    /// <summary>La clave es correcta pero el hash usa un formato o parámetros anteriores: hay que volver a calcularlo.</summary>
    CorrectaDebeActualizarse,
}

/// <summary>
/// Hash de claves. La implementación (Infraestructura) usa PasswordHasher de ASP.NET Core Identity.
/// El hash es un texto que incluye el formato, la sal y los parámetros; la clave nunca se guarda.
/// </summary>
public interface IHasherClaves
{
    string Calcular(string clave);
    ResultadoClave Verificar(string hash, string clave);
}
