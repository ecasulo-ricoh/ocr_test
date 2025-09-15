using OCR_test.Models.DTOs.DocuWare;

namespace OCR_test.Services.Implementations
{
    /// <summary>
    /// Métodos auxiliares para el servicio de bulk update
    /// </summary>
    public static class BulkUpdateHelpers
    {
        /// <summary>
        /// Extrae el nombre del campo de un mensaje de advertencia
        /// </summary>
        public static string ExtractFieldNameFromWarning(string warning)
        {
            // Buscar patrones como "NDEG_FACTURA formato inválido"
            var match = System.Text.RegularExpressions.Regex.Match(warning, @"^(\w+)\s+formato inválido");
            return match.Success ? match.Groups[1].Value : "Unknown";
        }

        /// <summary>
        /// Extrae el valor detectado de un mensaje de advertencia
        /// </summary>
        public static string ExtractValueFromWarning(string warning)
        {
            // Buscar patrones como "formato inválido: 'valor'"
            var match = System.Text.RegularExpressions.Regex.Match(warning, @"formato inválido:\s*'([^']*)'");
            return match.Success ? match.Groups[1].Value : "Unknown";
        }
    }
}