namespace Jimaco.Remisiones.Negocio.Servicios;

// Dígito de verificación del NIT según el algoritmo de la DIAN (módulo 11 con los primos 3, 7,
// 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71, aplicados de derecha a izquierda).
public static class DigitoVerificacion
{
    private static readonly int[] Pesos = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    public static string? Calcular(string nit)
    {
        var digitos = new string((nit ?? "").Where(char.IsDigit).ToArray());
        if (digitos.Length == 0 || digitos.Length > Pesos.Length)
            return null;

        var suma = 0;
        for (var i = 0; i < digitos.Length; i++)
            suma += (digitos[digitos.Length - 1 - i] - '0') * Pesos[i];

        var residuo = suma % 11;
        var dv = residuo > 1 ? 11 - residuo : residuo;
        return dv.ToString();
    }
}
