using OCR_test.Models.DTOs.DocuWare;

namespace OCR_test.Services.Interfaces
{
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