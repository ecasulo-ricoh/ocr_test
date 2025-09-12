using OCR_test.Models.DTOs.Invoice;

namespace OCR_test.Services.Interfaces
{
    /// <summary>
    /// Servicio para an�lisis estructurado de facturas
    /// </summary>
    public interface IInvoiceAnalysisService
    {
        /// <summary>
        /// Analiza un documento de DocuWare como factura
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fileCabinetId">ID del FileCabinet</param>
        /// <param name="language">Idioma para OCR</param>
        /// <returns>An�lisis estructurado de la factura</returns>
        Task<InvoiceAnalysisResultDto> AnalyzeInvoiceFromDocumentAsync(
            int documentId, 
            string? fileCabinetId = null, 
            string? language = null);

        /// <summary>
        /// Analiza un documento de DocuWare como factura (modo r�pido - solo primera p�gina)
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fileCabinetId">ID del FileCabinet</param>
        /// <param name="language">Idioma para OCR</param>
        /// <param name="fastMode">Si es true, solo procesa la primera p�gina</param>
        /// <returns>An�lisis estructurado de la factura</returns>
        Task<InvoiceAnalysisResultDto> AnalyzeInvoiceFromDocumentAsync(
            int documentId, 
            string? fileCabinetId = null, 
            string? language = null,
            bool fastMode = true);

        /// <summary>
        /// Analiza un archivo subido como factura
        /// </summary>
        /// <param name="stream">Stream del archivo</param>
        /// <param name="language">Idioma para OCR</param>
        /// <param name="fastMode">Si es true, solo procesa la primera p�gina</param>
        /// <returns>An�lisis estructurado de la factura</returns>
        Task<InvoiceAnalysisResultDto> AnalyzeInvoiceFromStreamAsync(
            Stream stream, 
            string? language = null,
            bool fastMode = true);

        /// <summary>
        /// Analiza texto ya extra�do como factura
        /// </summary>
        /// <param name="extractedText">Texto extra�do por OCR</param>
        /// <returns>An�lisis estructurado del texto como factura</returns>
        InvoiceAnalysisResultDto AnalyzeInvoiceFromText(string extractedText);

        // *** NUEVOS M�TODOS SIMPLIFICADOS ***
        
        /// <summary>
        /// An�lisis simplificado de factura desde documento - solo campos esenciales
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fileCabinetId">ID del FileCabinet</param>
        /// <param name="language">Idioma para OCR</param>
        /// <returns>An�lisis simplificado con solo los campos esenciales</returns>
        Task<SimplifiedInvoiceResultDto> AnalyzeInvoiceSimplifiedAsync(
            int documentId,
            string? fileCabinetId = null,
            string? language = null);

        /// <summary>
        /// An�lisis simplificado de factura desde stream - solo campos esenciales
        /// </summary>
        /// <param name="stream">Stream del archivo</param>
        /// <param name="language">Idioma para OCR</param>
        /// <returns>An�lisis simplificado con solo los campos esenciales</returns>
        Task<SimplifiedInvoiceResultDto> AnalyzeInvoiceSimplifiedFromStreamAsync(
            Stream stream,
            string? language = null);

        /// <summary>
        /// An�lisis simplificado de texto - solo campos esenciales
        /// </summary>
        /// <param name="extractedText">Texto extra�do por OCR</param>
        /// <returns>An�lisis simplificado con solo los campos esenciales</returns>
        SimplifiedInvoiceResultDto AnalyzeInvoiceSimplifiedFromText(string extractedText);
    }
}