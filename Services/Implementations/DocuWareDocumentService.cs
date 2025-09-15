using DocuWare.Platform.ServerClient;
using OCR_test.Models.DTOs.DocuWare;
using OCR_test.Services.Interfaces;

namespace OCR_test.Services.Implementations
{
    /// <summary>
    /// Implementación simplificada del servicio de documentos de DocuWare
    /// </summary>
    public class DocuWareDocumentService : IDocuWareDocumentService
    {
        private readonly IDocuWareConnectionService _connectionService;
        private readonly IDocuWareConfigurationService _configService;
        private readonly ILogger<DocuWareDocumentService> _logger;

        public DocuWareDocumentService(
            IDocuWareConnectionService connectionService,
            IDocuWareConfigurationService configService,
            ILogger<DocuWareDocumentService> logger)
        {
            _connectionService = connectionService;
            _configService = configService;
            _logger = logger;
        }

        public async Task<DocumentDownloadDto> ViewDocumentAsync(int documentId, string? fileCabinetId = null)
        {
            try
            {
                var fcId = fileCabinetId ?? _configService.GetFileCabinetId();
                _logger.LogInformation("Preparando visualización del documento {DocumentId}", documentId);

                var connection = _connectionService.GetConnection();
                var documentResponse = await connection.GetFromDocumentForDocumentAsync(documentId, fcId);
                var document = documentResponse.Content.GetDocumentFromSelfRelation();

                // Obtener el contenido del documento para visualización
                var downloadResponse = await document.PostToFileDownloadRelationForStreamAsync(
                    new FileDownload()
                    {
                        TargetFileType = FileDownloadType.Auto,
                    });

                var fileName = !string.IsNullOrWhiteSpace(document.Title) 
                    ? $"{document.Title}.pdf" 
                    : $"documento_{documentId}.pdf";

                return new DocumentDownloadDto
                {
                    Content = downloadResponse.Content,
                    ContentType = downloadResponse.ContentHeaders?.ContentType?.MediaType ?? "application/pdf",
                    FileName = fileName,
                    ContentLength = downloadResponse.ContentHeaders?.ContentLength
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparando visualización del documento {DocumentId}", documentId);
                throw;
            }
        }
    }
}