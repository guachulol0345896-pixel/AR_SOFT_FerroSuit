using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FerroSuite.Infraestructura.IA.Gemini;

/// <summary>Cifrado reversible de datos locales (para no guardar secretos en texto plano).</summary>
public interface IProtectorDatos
{
    bool Disponible { get; }
    byte[] Proteger(byte[] datos);
    byte[] Desproteger(byte[] datos);
}

/// <summary>
/// DPAPI de Windows (CryptProtectData/CryptUnprotectData de crypt32.dll), ámbito del usuario actual, con entropía propia
/// de FerroSuite. Se invoca directamente la API de Windows para no agregar paquetes. En otros sistemas no está disponible.
/// </summary>
public sealed class ProtectorDpapi : IProtectorDatos
{
    public static readonly ProtectorDpapi Instancia = new();
    private static readonly byte[] Entropia = "FerroSuite.ClaveGemini.v1"u8.ToArray();
    private const int SinInterfaz = 0x1; // CRYPTPROTECT_UI_FORBIDDEN

    public bool Disponible => OperatingSystem.IsWindows();

    public byte[] Proteger(byte[] datos) => Ejecutar(datos, proteger: true);
    public byte[] Desproteger(byte[] datos) => Ejecutar(datos, proteger: false);

    private byte[] Ejecutar(byte[] datos, bool proteger)
    {
        if (!Disponible) throw new PlatformNotSupportedException("DPAPI solo existe en Windows.");
        var entrada = Bloque.De(datos);
        var entropia = Bloque.De(Entropia);
        var salida = default(Bloque);
        try
        {
            var ok = proteger
                ? CryptProtectData(ref entrada, "FerroSuite", ref entropia, IntPtr.Zero, IntPtr.Zero, SinInterfaz, out salida)
                : CryptUnprotectData(ref entrada, IntPtr.Zero, ref entropia, IntPtr.Zero, IntPtr.Zero, SinInterfaz, out salida);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            var resultado = new byte[salida.Tamano];
            Marshal.Copy(salida.Datos, resultado, 0, salida.Tamano);
            return resultado;
        }
        finally
        {
            entrada.Liberar();
            entropia.Liberar();
            if (salida.Datos != IntPtr.Zero) LocalFree(salida.Datos);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bloque
    {
        public int Tamano;
        public IntPtr Datos;

        public static Bloque De(byte[] bytes)
        {
            var b = new Bloque { Tamano = bytes.Length, Datos = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
            Marshal.Copy(bytes, 0, b.Datos, bytes.Length);
            return b;
        }

        public void Liberar()
        {
            if (Datos == IntPtr.Zero) return;
            for (var i = 0; i < Tamano; i++) Marshal.WriteByte(Datos, i, 0); // no dejar el secreto en memoria no administrada
            Marshal.FreeHGlobal(Datos);
            Datos = IntPtr.Zero;
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Bloque entrada, string descripcion, ref Bloque entropia,
        IntPtr reservado, IntPtr aviso, int opciones, out Bloque salida);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Bloque entrada, IntPtr descripcion, ref Bloque entropia,
        IntPtr reservado, IntPtr aviso, int opciones, out Bloque salida);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memoria);
}
