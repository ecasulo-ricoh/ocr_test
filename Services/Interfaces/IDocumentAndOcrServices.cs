using OCR_test.Models.DTOs.DocuWare;

namespace OCR_test.Services.Interfaces
{
    /// <summary>
    /// Servicio para operaciones con documentos en DocuWare
    /// </summary>
    public interface IDocuWareDocumentService
    {
        /// <summary>
        /// Obtiene un documento específico por su ID
        /// </summary>
        Task<DocumentDto> GetDocumentAsync(int documentId, string? fileCabinetId = null);

        /// <summary>
        /// Visualiza un documento para navegador
        /// </summary>
        Task<DocumentDownloadDto> ViewDocumentAsync(int documentId, string? fileCabinetId = null);

        /// <summary>
        /// Descarga un documento
        /// </summary>
        Task<DocumentDownloadDto> DownloadDocumentAsync(int documentId, string? fileCabinetId = null);
    }

    /// <summary>
    /// Servicio para operaciones de OCR
    /// </summary>
    public interface IOcrService
    {
        /// <summary>
        /// Extrae texto de un stream de imagen/PDF
        /// </summary>
        Task<OcrResultDto> ExtractTextFromStreamAsync(Stream stream, string? language = null);

        /// <summary>
        /// Extrae texto de un documento de DocuWare
        /// </summary>
        Task<OcrResultDto> ExtractTextFromDocumentAsync(int documentId, string? fileCabinetId = null);
    }
}