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
    /// Servicio para actualizaci�n masiva de documentos en DocuWare con OCR
    /// </summary>
    public interface IDocuWareBulkUpdateService
    {
        /// <summary>
        /// Actualiza m�ltiples documentos en DocuWare usando datos de OCR
        /// </summary>
        /// <param name="request">Par�metros de la actualizaci�n masiva</param>
        /// <returns>Resultado detallado de la operaci�n</returns>
        Task<BulkUpdateResultDto> BulkUpdateDocumentsAsync(BulkUpdateInternalRequestDto request);

        /// <summary>
        /// Obtiene la lista de documentos a procesar
        /// </summary>
        /// <param name="fileCabinetId">ID del FileCabinet</param>
        /// <param name="count">Cantidad de documentos</param>
        /// <returns>Lista de IDs de documentos</returns>
        Task<List<int>> GetDocumentListAsync(string fileCabinetId, int count);

        /// <summary>
        /// Actualiza un documento espec�fico con los campos de OCR
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fields">Campos a actualizar</param>
        /// <param name="fileCabinetId">ID del FileCabinet</param>
        /// <param name="dryRun">Si es true, solo simula la actualizaci�n</param>
        /// <returns>Resultado de la actualizaci�n</returns>
        Task<DocumentUpdateDetailDto> UpdateDocumentFieldsAsync(
            int documentId, 
            DocumentUpdateFieldsDto fields, 
            string fileCabinetId, 
            bool dryRun = false);
    }
}