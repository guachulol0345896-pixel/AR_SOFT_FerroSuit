using System.Security.Cryptography;

namespace FerroSuite.Aplicacion.Seguridad;

/// <summary>Claves temporales aleatorias (generador criptográfico), sin caracteres que se confundan (0/O, 1/l/I).</summary>
public static class GeneradorClaves
{
    private const string Letras = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digitos = "23456789";

    public static string Nueva(int largo = 16)
    {
        var todos = Letras + Digitos;
        var c = new char[largo];
        for (var i = 0; i < largo; i++) c[i] = todos[RandomNumberGenerator.GetInt32(todos.Length)];
        // Garantiza al menos una letra y un dígito (la política de claves los exige).
        c[RandomNumberGenerator.GetInt32(largo)] = Letras[RandomNumberGenerator.GetInt32(Letras.Length)];
        var posDigito = RandomNumberGenerator.GetInt32(largo);
        c[posDigito] = Digitos[RandomNumberGenerator.GetInt32(Digitos.Length)];
        var tieneLetra = c.Any(char.IsLetter);
        if (!tieneLetra) c[(posDigito + 1) % largo] = Letras[RandomNumberGenerator.GetInt32(Letras.Length)];
        return new string(c);
    }
}
