using DocuWare.Platform.ServerClient;
using OCR_test.Models.DTOs.DocuWare;

namespace OCR_test.Services.Interfaces
{
    /// <summary>
    /// Servicio para gestionar la conexión con DocuWare
    /// </summary>
    public interface IDocuWareConnectionService
    {
        ServiceConnection GetConnection();
        Task<Organization[]> GetOrganizationsAsync();
        void Dispose();
    }

    /// <summary>
    /// Servicio para acceder a la configuración de DocuWare
    /// </summary>
    public interface IDocuWareConfigurationService
    {
        string GetFileCabinetId();
        string GetDocuWareUri();
        string GetUser();
        string GetPassword();
    }

    /// <summary>
    /// Servicio para actualización masiva de documentos en DocuWare con OCR
    /// </summary>
    public interface IDocuWareBulkUpdateService
    {
        /// <summary>
        /// Actualiza múltiples documentos en DocuWare usando datos de OCR
        /// </summary>
        /// <param name="request">Parámetros de la actualización masiva</param>
        /// <returns>Resultado detallado de la operación</returns>
        Task<BulkUpdateResultDto> BulkUpdateDocumentsAsync(BulkUpdateInternalRequestDto request);

        /// <summary>
        /// Obtiene la lista de documentos a procesar
        /// </summary>
        /// <param name="fileCabinetId">ID del FileCabinet</param>
        /// <param name="count">Cantidad de documentos</param>
        /// <returns>Lista de IDs de documentos</returns>
        Task<List<int>> GetDocumentListAsync(string fileCabinetId, int count);

        /// <summary>
        /// Actualiza un documento específico con los campos de OCR
        /// </summary>
        /// <param name="documentId">ID del documento</param>
        /// <param name="fields">Campos a actualizar</param>
        /// <param name="fileCabinetId">ID del FileCabinet</param>
        /// <param name="dryRun">Si es true, solo simula la actualización</param>
        /// <returns>Resultado de la actualización</returns>
        Task<DocumentUpdateDetailDto> UpdateDocumentFieldsAsync(
            int documentId, 
            DocumentUpdateFieldsDto fields, 
            string fileCabinetId, 
            bool dryRun = false);
    }
}