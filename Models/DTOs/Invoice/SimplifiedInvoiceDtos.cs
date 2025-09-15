namespace OCR_test.Models.DTOs.Invoice
{
    /// <summary>
    /// DTO simplificado para análisis rápido de facturas argentinas
    /// Contiene solo los campos esenciales requeridos
    /// </summary>
    public class SimplifiedInvoiceResultDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        
        /// <summary>
        /// Datos esenciales de la factura
        /// </summary>
        public SimplifiedInvoiceDataDto? Data { get; set; }
        
        /// <summary>
        /// Advertencias si algún campo no se pudo detectar
        /// </summary>
        public List<string> Warnings { get; set; } = new();
        
        public DateTime ProcessedAt { get; set; }
        public long ProcessingTimeMs { get; set; }
    }

    /// <summary>
    /// Datos esenciales de factura argentina
    /// </summary>
    public class SimplifiedInvoiceDataDto
    {
        /// <summary>
        /// Tipo de factura: "A" o "B"
        /// </summary>
        public string? TipoFactura { get; set; }
        
        /// <summary>
        /// Código de la factura argentina: "001" para tipo A, "006" para tipo B
        /// </summary>
        public string? CodigoFactura { get; set; }
        
        /// <summary>
        /// Número de la factura (ej: "00704-00128327")
        /// </summary>
        public string? NroFactura { get; set; }
        
        /// <summary>
        /// Fecha de la factura en formato DD/mm/yyyy (ej: "20/05/2025")
        /// </summary>
        public string? FechaFactura { get; set; }
        
        /// <summary>
        /// CUIT del cliente (SIEMPRE el segundo CUIT que aparece en el documento)
        /// </summary>
        public string? CuitCliente { get; set; }
        
        /// <summary>
        /// Indica si se requiere actualización manual en DocuWare
        /// </summary>
        public bool RequiereActualizacionManual { get; set; }
        
        /// <summary>
        /// Confianza general del análisis (0-1)
        /// </summary>
        public float Confianza { get; set; }
    }
}