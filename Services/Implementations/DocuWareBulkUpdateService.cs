using DocuWare.Platform.ServerClient;
using OCR_test.Models.DTOs.DocuWare;
using OCR_test.Services.Interfaces;
using System.Diagnostics;

namespace OCR_test.Services.Implementations
{
    /// <summary>
    /// Implementación del servicio de actualización masiva de documentos en DocuWare
    /// </summary>
    public class DocuWareBulkUpdateService : IDocuWareBulkUpdateService
    {
        private readonly IDocuWareConnectionService _connectionService;
        private readonly IDocuWareConfigurationService _configService;
        private readonly IInvoiceAnalysisService _invoiceAnalysisService;
        private readonly ILogger<DocuWareBulkUpdateService> _logger;

        public DocuWareBulkUpdateService(
            IDocuWareConnectionService connectionService,
            IDocuWareConfigurationService configService,
            IInvoiceAnalysisService invoiceAnalysisService,
            ILogger<DocuWareBulkUpdateService> logger)
        {
            _connectionService = connectionService;
            _configService = configService;
            _invoiceAnalysisService = invoiceAnalysisService;
            _logger = logger;
        }

        public async Task<BulkUpdateResultDto> BulkUpdateDocumentsAsync(BulkUpdateInternalRequestDto request)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new BulkUpdateResultDto
            {
                Metadata = new BulkUpdateMetadataDto
                {
                    StartTime = DateTime.UtcNow,
                    DryRunMode = request.DryRun,
                    RequestedDocumentCount = request.DocumentCount,
                    Language = request.Language,
                    FileCabinetId = request.FileCabinetId
                }
            };

            try
            {
                var mode = request.DryRun ? "DRY-RUN" : "ACTUALIZACIÓN REAL";
                _logger.LogInformation("?? Iniciando actualización masiva en modo {Mode}. Documentos a procesar: {Count}", 
                    mode, request.DocumentCount);

                // Obtener lista de documentos
                var documentIds = await GetDocumentListAsync(result.Metadata.FileCabinetId, request.DocumentCount);
                
                if (!documentIds.Any())
                {
                    result.Success = false;
                    result.Message = "No se encontraron documentos para procesar";
                    return result;
                }

                _logger.LogInformation("?? Documentos encontrados: {Count}. IDs: [{DocumentIds}]", 
                    documentIds.Count, string.Join(", ", documentIds));

                var ocrTimes = new List<long>();
                var updateTimes = new List<long>();

                // Procesar cada documento
                for (int i = 0; i < documentIds.Count; i++)
                {
                    var documentId = documentIds[i];
                    var docStopwatch = Stopwatch.StartNew();

                    try
                    {
                        _logger.LogInformation("?? Procesando documento {Current}/{Total}: ID {DocumentId}", 
                            i + 1, documentIds.Count, documentId);

                        // 1. Ejecutar OCR en el documento
                        var ocrStopwatch = Stopwatch.StartNew();
                        var ocrResult = await _invoiceAnalysisService.AnalyzeInvoiceSimplifiedAsync(
                            documentId, result.Metadata.FileCabinetId, request.Language);
                        ocrStopwatch.Stop();
                        ocrTimes.Add(ocrStopwatch.ElapsedMilliseconds);

                        var detail = new DocumentUpdateDetailDto
                        {
                            DocumentId = documentId,
                            ProcessingTimeMs = docStopwatch.ElapsedMilliseconds
                        };

                        if (!ocrResult.Success || ocrResult.Data == null)
                        {
                            detail.Status = "Failed";
                            detail.Message = $"Error en OCR: {ocrResult.Message}";
                            detail.Errors.Add($"OCR falló: {ocrResult.Message}");
                            result.FailedUpdates++;
                            result.Errors.Add($"Documento {documentId}: Error en OCR");
                        }
                        else
                        {
                            // 2. Mapear campos de OCR a campos de DocuWare
                            detail.DetectedFields = new DocumentOcrFieldsDto
                            {
                                TipoFactura = ocrResult.Data.TipoFactura,
                                CodigoFactura = ocrResult.Data.CodigoFactura,
                                NroFactura = ocrResult.Data.NroFactura,
                                FechaFactura = ocrResult.Data.FechaFactura,
                                CuitCliente = ocrResult.Data.CuitCliente,
                                RazonSocialCliente = ocrResult.Data.RazonSocialCliente,
                                Confianza = ocrResult.Data.Confianza
                            };

                            var updateFields = new DocumentUpdateFieldsDto
                            {
                                LETRA_DOCUMENTO = ocrResult.Data.TipoFactura,
                                CODIGO_DOCUMENTO = ocrResult.Data.CodigoFactura,
                                NDEG_FACTURA = ocrResult.Data.NroFactura,
                                DATE = ocrResult.Data.FechaFactura,
                                CUIT_CLIENTE = ocrResult.Data.CuitCliente
                                // RAZON_SOCIAL intencionalmente omitido
                            };

                            // 3. Verificar si hay campos para actualizar
                            if (HasFieldsToUpdate(updateFields))
                            {
                                // 4. Actualizar documento en DocuWare
                                var updateStopwatch = Stopwatch.StartNew();
                                var updateResult = await UpdateDocumentFieldsAsync(
                                    documentId, updateFields, result.Metadata.FileCabinetId, request.DryRun);
                                updateStopwatch.Stop();
                                updateTimes.Add(updateStopwatch.ElapsedMilliseconds);

                                detail.Status = updateResult.Status;
                                detail.Message = updateResult.Message;
                                detail.UpdatedFields = updateFields;
                                detail.Errors.AddRange(updateResult.Errors);

                                if (updateResult.Status == "Success")
                                {
                                    result.SuccessfulUpdates++;
                                }
                                else if (updateResult.Status == "Failed")
                                {
                                    result.FailedUpdates++;
                                    result.Errors.Add($"Documento {documentId}: Error en actualización");
                                }
                                else
                                {
                                    result.SkippedDocuments++;
                                }
                            }
                            else
                            {
                                detail.Status = "NoChanges";
                                detail.Message = "No se encontraron campos válidos para actualizar";
                                result.SkippedDocuments++;
                            }
                        }

                        docStopwatch.Stop();
                        detail.ProcessingTimeMs = docStopwatch.ElapsedMilliseconds;
                        result.Details.Add(detail);
                        result.TotalProcessed++;

                        // Log de progreso cada 10 documentos
                        if ((i + 1) % 10 == 0 || i == documentIds.Count - 1)
                        {
                            _logger.LogInformation("?? Progreso: {Current}/{Total} documentos procesados. " +
                                "Exitosos: {Success}, Fallidos: {Failed}, Omitidos: {Skipped}",
                                i + 1, documentIds.Count, result.SuccessfulUpdates, result.FailedUpdates, result.SkippedDocuments);
                        }
                    }
                    catch (Exception ex)
                    {
                        docStopwatch.Stop();
                        _logger.LogError(ex, "Error procesando documento {DocumentId}", documentId);

                        result.Details.Add(new DocumentUpdateDetailDto
                        {
                            DocumentId = documentId,
                            Status = "Failed",
                            Message = $"Error inesperado: {ex.Message}",
                            ProcessingTimeMs = docStopwatch.ElapsedMilliseconds,
                            Errors = { ex.Message }
                        });

                        result.FailedUpdates++;
                        result.TotalProcessed++;
                        result.Errors.Add($"Documento {documentId}: Error inesperado");
                    }
                }

                stopwatch.Stop();

                // Calcular estadísticas de rendimiento
                result.Metadata.EndTime = DateTime.UtcNow;
                result.Metadata.TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                result.Metadata.Performance = new PerformanceStatsDto
                {
                    AverageOcrTimeMs = ocrTimes.Any() ? ocrTimes.Average() : 0,
                    AverageUpdateTimeMs = updateTimes.Any() ? updateTimes.Average() : 0,
                    DocumentsPerSecond = result.TotalProcessed / (stopwatch.ElapsedMilliseconds / 1000.0),
                    TotalOcrTimeMs = ocrTimes.Sum(),
                    TotalUpdateTimeMs = updateTimes.Sum()
                };

                result.Success = true;
                result.Message = $"Procesamiento completado en modo {mode}. " +
                    $"Procesados: {result.TotalProcessed}, Exitosos: {result.SuccessfulUpdates}, " +
                    $"Fallidos: {result.FailedUpdates}, Omitidos: {result.SkippedDocuments}";

                _logger.LogInformation("? Actualización masiva completada en {ElapsedMs}ms. {Message}",
                    stopwatch.ElapsedMilliseconds, result.Message);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error en actualización masiva");

                result.Success = false;
                result.Message = $"Error en actualización masiva: {ex.Message}";
                result.Errors.Add(ex.Message);
                result.Metadata.EndTime = DateTime.UtcNow;
                result.Metadata.TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds;

                return result;
            }
        }

        public async Task<List<int>> GetDocumentListAsync(string fileCabinetId, int count)
        {
            try
            {
                _logger.LogInformation("?? Obteniendo lista de {Count} documentos del FileCabinet {FileCabinetId}", 
                    count, fileCabinetId);

                var connection = _connectionService.GetConnection();

                try
                {
                    // Intentar obtener documentos reales usando la API de DocuWare
                    var organizations = await connection.GetOrganizationsAsync();
                    var organization = organizations.FirstOrDefault();

                    if (organization == null)
                    {
                        throw new InvalidOperationException("No se encontró organización en DocuWare");
                    }

                    _logger.LogInformation("?? Organización encontrada: {OrgName}", organization.Name);

                    // Esta es la implementación real que deberías usar en producción
                    // Por ahora, como no tenemos acceso directo a la API completa de DocuWare,
                    // vamos a simular la obtención de documentos

                    _logger.LogInformation("??  Usando implementación de demostración para obtener documentos");
                    _logger.LogInformation("?? En producción, esto usaría la API real de búsqueda de DocuWare");

                    // Generar IDs de documentos de demostración
                    var documentIds = new List<int>();
                    var baseId = 1; // En producción, estos serían IDs reales de DocuWare
                    
                    for (int i = 0; i < count; i++)
                    {
                        documentIds.Add(baseId + i);
                    }

                    _logger.LogInformation("? Lista de {Count} documentos generada: [{DocumentIds}]", 
                        documentIds.Count, string.Join(", ", documentIds.Take(10)) + (documentIds.Count > 10 ? "..." : ""));

                    _logger.LogInformation("?? Para producción, implementar búsqueda real con DialogExpression en FileCabinet");

                    return documentIds;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo conectar a DocuWare API, usando modo de demostración");
                    
                    // Fallback a modo de demostración
                    var documentIds = new List<int>();
                    for (int i = 1; i <= count; i++)
                    {
                        documentIds.Add(i);
                    }

                    _logger.LogInformation("?? Generados {Count} IDs de documentos para demostración: [{DocumentIds}]", 
                        documentIds.Count, string.Join(", ", documentIds));

                    return documentIds;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo lista de documentos");
                throw;
            }
        }

        public async Task<DocumentUpdateDetailDto> UpdateDocumentFieldsAsync(
            int documentId, 
            DocumentUpdateFieldsDto fields, 
            string fileCabinetId, 
            bool dryRun = false)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new DocumentUpdateDetailDto
            {
                DocumentId = documentId,
                UpdatedFields = fields
            };

            try
            {
                if (dryRun)
                {
                    _logger.LogInformation("?? DRY-RUN - Documento {DocumentId}: Campos a actualizar: {Fields}",
                        documentId, SerializeFields(fields));

                    result.Status = "Success";
                    result.Message = "DRY-RUN: Actualización simulada exitosamente";
                    stopwatch.Stop();
                    result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                    return result;
                }

                var connection = _connectionService.GetConnection();
                
                // Obtener el documento
                var documentResponse = await connection.GetFromDocumentForDocumentAsync(documentId, fileCabinetId);
                var document = documentResponse.Content.GetDocumentFromSelfRelation();

                // Preparar campos para actualización usando la sintaxis correcta
                var fieldsToUpdate = new List<DocumentIndexField>();

                if (!string.IsNullOrEmpty(fields.LETRA_DOCUMENTO))
                {
                    fieldsToUpdate.Add(DocumentIndexField.Create("LETRA_DOCUMENTO", fields.LETRA_DOCUMENTO));
                    _logger.LogInformation("?? Preparando campo LETRA_DOCUMENTO: {Value}", fields.LETRA_DOCUMENTO);
                }

                if (!string.IsNullOrEmpty(fields.CODIGO_DOCUMENTO))
                {
                    fieldsToUpdate.Add(DocumentIndexField.Create("CODIGO_DOCUMENTO", fields.CODIGO_DOCUMENTO));
                    _logger.LogInformation("?? Preparando campo CODIGO_DOCUMENTO: {Value}", fields.CODIGO_DOCUMENTO);
                }

                if (!string.IsNullOrEmpty(fields.NDEG_FACTURA))
                {
                    fieldsToUpdate.Add(DocumentIndexField.Create("NDEG_FACTURA", fields.NDEG_FACTURA));
                    _logger.LogInformation("?? Preparando campo NDEG_FACTURA: {Value}", fields.NDEG_FACTURA);
                }

                if (!string.IsNullOrEmpty(fields.DATE))
                {
                    // Intentar convertir la fecha del formato DD/MM/yyyy a DateTime
                    if (DateTime.TryParseExact(fields.DATE, "dd/MM/yyyy", 
                        System.Globalization.CultureInfo.InvariantCulture, 
                        System.Globalization.DateTimeStyles.None, out var parsedDate))
                    {
                        fieldsToUpdate.Add(DocumentIndexField.CreateDate("DATE", parsedDate));
                        _logger.LogInformation("?? Preparando campo DATE: {Value} -> {ParsedDate}", fields.DATE, parsedDate);
                    }
                    else
                    {
                        // Si no se puede parsear, guardarlo como string
                        fieldsToUpdate.Add(DocumentIndexField.Create("DATE", fields.DATE));
                        _logger.LogWarning("??  Campo DATE no se pudo parsear como fecha, guardando como string: {Value}", fields.DATE);
                    }
                }

                if (!string.IsNullOrEmpty(fields.CUIT_CLIENTE))
                {
                    fieldsToUpdate.Add(DocumentIndexField.Create("CUIT_CLIENTE", fields.CUIT_CLIENTE));
                    _logger.LogInformation("?? Preparando campo CUIT_CLIENTE: {Value}", fields.CUIT_CLIENTE);
                }

                if (fieldsToUpdate.Any())
                {
                    // Crear el objeto DocumentIndexFields
                    var fieldsUpdate = new DocumentIndexFields
                    {
                        Field = fieldsToUpdate
                    };

                    _logger.LogInformation("?? Actualizando documento {DocumentId} con {Count} campos en DocuWare...",
                        documentId, fieldsToUpdate.Count);

                    // Ejecutar actualización usando la sintaxis correcta
                    var updateResult = await document.PutToFieldsRelationForDocumentIndexFieldsAsync(fieldsUpdate);

                    _logger.LogInformation("? Documento {DocumentId} actualizado exitosamente en DocuWare con {Count} campos",
                        documentId, fieldsToUpdate.Count);

                    result.Status = "Success";
                    result.Message = $"Documento actualizado exitosamente con {fieldsToUpdate.Count} campos en DocuWare";
                }
                else
                {
                    _logger.LogInformation("??  No hay campos válidos para actualizar en documento {DocumentId}", documentId);
                    result.Status = "NoChanges";
                    result.Message = "No hay campos válidos para actualizar";
                }

                stopwatch.Stop();
                result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "? Error actualizando documento {DocumentId} en DocuWare", documentId);

                result.Status = "Failed";
                result.Message = $"Error en actualización DocuWare: {ex.Message}";
                result.Errors.Add(ex.Message);
                result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                return result;
            }
        }

        private bool HasFieldsToUpdate(DocumentUpdateFieldsDto fields)
        {
            return !string.IsNullOrEmpty(fields.LETRA_DOCUMENTO) ||
                   !string.IsNullOrEmpty(fields.CODIGO_DOCUMENTO) ||
                   !string.IsNullOrEmpty(fields.NDEG_FACTURA) ||
                   !string.IsNullOrEmpty(fields.DATE) ||
                   !string.IsNullOrEmpty(fields.CUIT_CLIENTE);
        }

        private string SerializeFields(DocumentUpdateFieldsDto fields)
        {
            var fieldsList = new List<string>();
            
            if (!string.IsNullOrEmpty(fields.LETRA_DOCUMENTO))
                fieldsList.Add($"LETRA_DOCUMENTO={fields.LETRA_DOCUMENTO}");
            
            if (!string.IsNullOrEmpty(fields.CODIGO_DOCUMENTO))
                fieldsList.Add($"CODIGO_DOCUMENTO={fields.CODIGO_DOCUMENTO}");
            
            if (!string.IsNullOrEmpty(fields.NDEG_FACTURA))
                fieldsList.Add($"NDEG_FACTURA={fields.NDEG_FACTURA}");
            
            if (!string.IsNullOrEmpty(fields.DATE))
                fieldsList.Add($"DATE={fields.DATE}");
            
            if (!string.IsNullOrEmpty(fields.CUIT_CLIENTE))
                fieldsList.Add($"CUIT_CLIENTE={fields.CUIT_CLIENTE}");

            return string.Join(", ", fieldsList);
        }

        /// <summary>
        /// Ejemplo de implementación real para búsqueda de documentos en DocuWare
        /// Este método muestra cómo debería implementarse en producción
        /// </summary>
        private async Task<List<int>> GetDocumentListFromDocuWareAsync(string fileCabinetId, int count)
        {
            // NOTA: Esta es la implementación que deberías usar en producción
            // cuando tengas acceso completo a la API de DocuWare

            var connection = _connectionService.GetConnection();
            var organizations = await connection.GetOrganizationsAsync();
            var organization = organizations.FirstOrDefault();

            if (organization == null)
            {
                throw new InvalidOperationException("No se encontró organización en DocuWare");
            }

            // Obtener FileCabinet específico
            // var fileCabinet = await organization.GetFileCabinetFromFileCabinetsRelation(fileCabinetId);

            // Obtener diálogos de búsqueda
            // var dialogs = await fileCabinet.GetDialogInfosFromDialogsRelation();
            // var searchDialog = dialogs.Dialog.FirstOrDefault(d => d.Type == DialogType.Search);

            // Crear expresión de búsqueda para obtener documentos más recientes
            /*
            var searchExpression = new DialogExpression()
            {
                Operation = DialogExpressionOperation.And,
                Condition = new List<DialogExpressionCondition>(),
                Count = count,
                SortOrder = new List<SortedField>
                {
                    new SortedField 
                    { 
                        Field = "DWSTOREDATETIME", 
                        Direction = SortDirection.Desc 
                    }
                }
            };

            // Ejecutar búsqueda
            var searchResult = await searchDialog.PostToDialogExpressionRelationForDocumentsQueryResultAsync(searchExpression);
            return searchResult.Items.Select(item => item.Id).ToList();
            */

            // Por ahora, devolver lista simulada
            throw new NotImplementedException("Implementación completa de DocuWare API pendiente");
        }
    }
}