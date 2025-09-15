using DocuWare.Platform.ServerClient;
using OCR_test.Models.DTOs.DocuWare;

namespace OCR_test.Services.Interfaces
{
    /// <summary>
    /// Servicio para gestionar la conexi�n con DocuWare
    /// </summary>
    public interface IDocuWareConnectionService
    {
        ServiceConnection GetConnection();
        Task<Organization[]> GetOrganizationsAsync();
        void Dispose();
    }

    /// <summary>
    /// Servicio para acceder a la configuraci�n de DocuWare
    /// </summary>
    public interface IDocuWareConfigurationService
    {
        string GetFileCabinetId();
        string GetDocuWareUri();
        string GetUser();
        string GetPassword();
    }

    /// <summary>
    /// Servicio para gesti�n de documentos DocuWare (solo visualizaci�n)
    /// </summary>
    public interface IDocuWareDocumentService
    {
        Task<DocumentDownloadDto> ViewDocumentAsync(int documentId, string? fileCabinetId = null);
    }

    /// <summary>
    /// Servicio para actualizaci�n masiva de documentos en DocuWare con OCR
    /// </summary>
    public interface IDocuWareBulkUpdateService
    {
        Task<BulkUpdateResultDto> BulkUpdateDocumentsAsync(BulkUpdateInternalRequestDto request);
        Task<List<int>> GetDocumentListAsync(string fileCabinetId, int count);
        Task<DocumentUpdateDetailDto> UpdateDocumentFieldsAsync(
            int documentId, 
            DocumentUpdateFieldsDto fields, 
            string fileCabinetId, 
            bool dryRun = false);
    }
}