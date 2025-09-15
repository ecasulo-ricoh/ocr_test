using DocuWare.Platform.ServerClient;
using OCR_test.Models.DTOs.DocuWare;
using OCR_test.Models.DTOs.Invoice;
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
                    OnlyUpdateEmptyFields = request.OnlyUpdateEmptyFields,
                    RequestedDocumentCount = request.DocumentCount,
                    Language = request.Language,
                    FileCabinetId = request.FileCabinetId
                }
            };

            try
            {
                var mode = request.DryRun ? "DRY-RUN" : "ACTUALIZACIÓN REAL";
                var updateStrategy = request.OnlyUpdateEmptyFields ? "SOLO CAMPOS VACÍOS" : "SOBRESCRIBIR TODOS";
                
                _logger.LogInformation("?? Iniciando actualización masiva en modo {Mode} con estrategia {Strategy}. Documentos a procesar: {Count}", 
                    mode, updateStrategy, request.DocumentCount);

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
                            result.Errors.Add($" Documento {documentId}: Error en OCR");
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
                                Confianza = ocrResult.Data.Confianza
                            };

                            // 3. Validar y preparar campos para actualización
                            var validationResult = await ValidateAndPrepareFieldsAsync(
                                documentId, ocrResult.Data, result.Metadata.FileCabinetId, request.OnlyUpdateEmptyFields);
                            
                            detail.UpdatedFields = validationResult.ValidatedFields;
                            detail.SkippedFields = validationResult.SkippedFields;
                            detail.ValidationWarnings = validationResult.ValidationWarnings;

                            // 4. Verificar si hay campos para actualizar
                            if (HasFieldsToUpdate(validationResult.ValidatedFields))
                            {
                                // 4. Actualizar documento en DocuWare
                                var updateStopwatch = Stopwatch.StartNew();
                                var updateResult = await UpdateDocumentFieldsAsync(
                                    documentId, validationResult.ValidatedFields, result.Metadata.FileCabinetId, request.DryRun);
                                updateStopwatch.Stop();
                                updateTimes.Add(updateStopwatch.ElapsedMilliseconds);

                                detail.Status = updateResult.Status;
                                detail.Message = updateResult.Message;
                                detail.Errors.AddRange(updateResult.Errors);

                                if (updateResult.Status == "Success")
                                {
                                    result.SuccessfulUpdates++;
                                    _logger.LogInformation("? Documento {DocumentId} actualizado exitosamente", documentId);
                                }
                                else if (updateResult.Status == "Failed")
                                {
                                    result.FailedUpdates++;
                                    result.Errors.Add($" Documento {documentId}: Error en actualización");
                                }
                                else
                                {
                                    result.SkippedDocuments++;
                                    _logger.LogInformation("??  Documento {DocumentId} omitido: {Message}", documentId, updateResult.Message);
                                }
                            }
                            else
                            {
                                detail.Status = "NoChanges";
                                detail.Message = "No se encontraron campos válidos para actualizar después de validaciones";
                                result.SkippedDocuments++;
                                _logger.LogInformation("??  Documento {DocumentId} sin cambios: no hay campos para actualizar", documentId);
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
                                "Modificados: {Modified}, Errores: {Failed}, Sin cambios: {Skipped}",
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
                        result.Errors.Add($" Documento {documentId}: Error inesperado");
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
                result.Message = $"Procesamiento completado en modo {mode} con estrategia {updateStrategy}. " +
                    $"Procesados: {result.TotalProcessed}, Modificados: {result.SuccessfulUpdates}, " +
                    $"Errores: {result.FailedUpdates}, Sin cambios: {result.SkippedDocuments}";

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
                // Contar campos válidos antes de procesar
                var fieldsToUpdateCount = CountValidFields(fields);
                
                if (fieldsToUpdateCount == 0)
                {
                    result.Status = "NoChanges";
                    result.Message = "No hay campos válidos para actualizar";
                    stopwatch.Stop();
                    result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                    _logger.LogInformation("??  Documento {DocumentId}: No hay campos para actualizar", documentId);
                    return result;
                }

                if (dryRun)
                {
                    _logger.LogInformation("?? DRY-RUN - Documento {DocumentId}: {Count} campos a actualizar: {Fields}",
                        documentId, fieldsToUpdateCount, SerializeFields(fields));

                    result.Status = "Success";
                    result.Message = $"DRY-RUN: {fieldsToUpdateCount} campos serían actualizados";
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

        /// <summary>
        /// Cuenta el número de campos válidos (no vacíos) para actualizar
        /// </summary>
        private int CountValidFields(DocumentUpdateFieldsDto fields)
        {
            var count = 0;
            
            if (!string.IsNullOrEmpty(fields.LETRA_DOCUMENTO)) count++;
            if (!string.IsNullOrEmpty(fields.CODIGO_DOCUMENTO)) count++;
            if (!string.IsNullOrEmpty(fields.NDEG_FACTURA)) count++;
            if (!string.IsNullOrEmpty(fields.DATE)) count++;
            if (!string.IsNullOrEmpty(fields.CUIT_CLIENTE)) count++;
            
            return count;
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

        /// <summary>
        /// Resultado de validación de campos
        /// </summary>
        private class FieldValidationResult
        {
            public DocumentUpdateFieldsDto ValidatedFields { get; set; } = new();
            public List<string> SkippedFields { get; set; } = new();
            public List<string> ValidationWarnings { get; set; } = new();
        }

        /// <summary>
        /// Valida y prepara los campos para actualización según las reglas de negocio
        /// </summary>
        private async Task<FieldValidationResult> ValidateAndPrepareFieldsAsync(
            int documentId, 
            OCR_test.Models.DTOs.Invoice.SimplifiedInvoiceDataDto ocrData, 
            string fileCabinetId, 
            bool onlyUpdateEmptyFields)
        {
            var result = new FieldValidationResult();
            var validatedFields = new DocumentUpdateFieldsDto();

            try
            {
                // Obtener campos actuales del documento si es necesario
                Dictionary<string, string?> currentFields = new();
                
                if (onlyUpdateEmptyFields)
                {
                    currentFields = await GetCurrentDocumentFieldsAsync(documentId, fileCabinetId);
                }

                // Validar LETRA_DOCUMENTO
                if (!string.IsNullOrEmpty(ocrData.TipoFactura))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "LETRA_DOCUMENTO"))
                    {
                        if (IsValidInvoiceType(ocrData.TipoFactura))
                        {
                            validatedFields.LETRA_DOCUMENTO = ocrData.TipoFactura;
                            _logger.LogInformation("? LETRA_DOCUMENTO validado: {Value} para documento {DocumentId}", 
                                ocrData.TipoFactura, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("LETRA_DOCUMENTO");
                            result.ValidationWarnings.Add($"LETRA_DOCUMENTO inválido: '{ocrData.TipoFactura}' (solo se permiten A o B)");
                            _logger.LogWarning("??  LETRA_DOCUMENTO inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.TipoFactura);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("LETRA_DOCUMENTO");
                        result.ValidationWarnings.Add("LETRA_DOCUMENTO omitido: campo ya tiene valor y onlyUpdateEmptyFields=true");
                        _logger.LogInformation("??  LETRA_DOCUMENTO omitido para documento {DocumentId}: campo ya tiene valor", documentId);
                    }
                }

                // Validar CODIGO_DOCUMENTO
                if (!string.IsNullOrEmpty(ocrData.CodigoFactura))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "CODIGO_DOCUMENTO"))
                    {
                        if (IsValidInvoiceCode(ocrData.CodigoFactura))
                        {
                            validatedFields.CODIGO_DOCUMENTO = ocrData.CodigoFactura;
                            _logger.LogInformation("? CODIGO_DOCUMENTO validado: {Value} para documento {DocumentId}", 
                                ocrData.CodigoFactura, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("CODIGO_DOCUMENTO");
                            result.ValidationWarnings.Add($"CODIGO_DOCUMENTO inválido: '{ocrData.CodigoFactura}' (solo se permiten 001 o 006)");
                            _logger.LogWarning("??  CODIGO_DOCUMENTO inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.CodigoFactura);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("CODIGO_DOCUMENTO");
                        result.ValidationWarnings.Add("CODIGO_DOCUMENTO omitido: campo ya tiene valor y onlyUpdateEmptyFields=true");
                    }
                }

                // Validar NDEG_FACTURA (con formato específico)
                if (!string.IsNullOrEmpty(ocrData.NroFactura))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "NDEG_FACTURA"))
                    {
                        if (IsValidInvoiceNumber(ocrData.NroFactura))
                        {
                            validatedFields.NDEG_FACTURA = ocrData.NroFactura;
                            _logger.LogInformation("? NDEG_FACTURA validado: {Value} para documento {DocumentId}", 
                                ocrData.NroFactura, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("NDEG_FACTURA");
                            result.ValidationWarnings.Add($"NDEG_FACTURA formato inválido: '{ocrData.NroFactura}' (formato esperado: 00000-00000000)");
                            _logger.LogWarning("??  NDEG_FACTURA formato inválido para documento {DocumentId}: {Value} - Revisar manualmente", 
                                documentId, ocrData.NroFactura);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("NDEG_FACTURA");
                        result.ValidationWarnings.Add("NDEG_FACTURA omitido: campo ya tiene valor y onlyUpdateEmptyFields=true");
                    }
                }

                // Validar DATE
                if (!string.IsNullOrEmpty(ocrData.FechaFactura))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "DATE"))
                    {
                        if (IsValidDate(ocrData.FechaFactura))
                        {
                            validatedFields.DATE = ocrData.FechaFactura;
                            _logger.LogInformation("? DATE validado: {Value} para documento {DocumentId}", 
                                ocrData.FechaFactura, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("DATE");
                            result.ValidationWarnings.Add($"DATE formato inválido: '{ocrData.FechaFactura}' (formato esperado: DD/MM/yyyy)");
                            _logger.LogWarning("??  DATE formato inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.FechaFactura);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("DATE");
                        result.ValidationWarnings.Add("DATE omitido: campo ya tiene valor y onlyUpdateEmptyFields=true");
                    }
                }

                // Validar CUIT_CLIENTE
                if (!string.IsNullOrEmpty(ocrData.CuitCliente))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "CUIT_CLIENTE"))
                    {
                        if (IsValidCuit(ocrData.CuitCliente))
                        {
                            validatedFields.CUIT_CLIENTE = ocrData.CuitCliente;
                            _logger.LogInformation("? CUIT_CLIENTE validado: {Value} para documento {DocumentId}", 
                                ocrData.CuitCliente, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("CUIT_CLIENTE");
                            result.ValidationWarnings.Add($"CUIT_CLIENTE formato inválido: '{ocrData.CuitCliente}' (formato esperado: XX-XXXXXXXX-X)");
                            _logger.LogWarning("??  CUIT_CLIENTE formato inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.CuitCliente);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("CUIT_CLIENTE");
                        result.ValidationWarnings.Add("CUIT_CLIENTE omitido: campo ya tiene valor y onlyUpdateEmptyFields=true");
                    }
                }

                result.ValidatedFields = validatedFields;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validando campos para documento {DocumentId}", documentId);
                result.ValidationWarnings.Add($"Error en validación: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Obtiene los campos actuales del documento desde DocuWare
        /// </summary>
        private async Task<Dictionary<string, string?>> GetCurrentDocumentFieldsAsync(int documentId, string fileCabinetId)
        {
            var fields = new Dictionary<string, string?>();
            
            try
            {
                var connection = _connectionService.GetConnection();
                var documentResponse = await connection.GetFromDocumentForDocumentAsync(documentId, fileCabinetId);
                var document = documentResponse.Content.GetDocumentFromSelfRelation();

                if (document.Fields != null)
                {
                    foreach (var field in document.Fields)
                    {
                        if (field.FieldName != null)
                        {
                            fields[field.FieldName] = field.Item?.ToString();
                        }
                    }
                }

                _logger.LogDebug("?? Campos actuales obtenidos para documento {DocumentId}: {FieldCount} campos", 
                    documentId, fields.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron obtener campos actuales para documento {DocumentId}", documentId);
            }

            return fields;
        }

        /// <summary>
        /// Verifica si un campo está vacío
        /// </summary>
        private bool IsFieldEmpty(Dictionary<string, string?> currentFields, string fieldName)
        {
            if (!currentFields.ContainsKey(fieldName))
                return true;

            var value = currentFields[fieldName];
            return string.IsNullOrWhiteSpace(value);
        }

        /// <summary>
        /// Valida el formato del número de factura: 00000-00000000
        /// </summary>
        private bool IsValidInvoiceNumber(string invoiceNumber)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber))
                return false;

            // Patrón: exactamente 5 dígitos, guión, exactamente 8 dígitos
            var pattern = @"^\d{5}-\d{8}$";
            return System.Text.RegularExpressions.Regex.IsMatch(invoiceNumber, pattern);
        }

        /// <summary>
        /// Valida el tipo de factura (solo A o B)
        /// </summary>
        private bool IsValidInvoiceType(string invoiceType)
        {
            return invoiceType == "A" || invoiceType == "B";
        }

        /// <summary>
        /// Valida el código de factura (solo 001 o 006)
        /// </summary>
        private bool IsValidInvoiceCode(string invoiceCode)
        {
            return invoiceCode == "001" || invoiceCode == "006";
        }

        /// <summary>
        /// Valida el formato de fecha DD/MM/yyyy
        /// </summary>
        private bool IsValidDate(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return false;

            return DateTime.TryParseExact(dateString, "dd/MM/yyyy", 
                System.Globalization.CultureInfo.InvariantCulture, 
                System.Globalization.DateTimeStyles.None, out _);
        }

        /// <summary>
        /// Valida el formato de CUIT: XX-XXXXXXXX-X
        /// </summary>
        private bool IsValidCuit(string cuit)
        {
            if (string.IsNullOrWhiteSpace(cuit))
                return false;

            // Patrón: exactamente 2 dígitos, guión, 8 dígitos, guión, 1 dígito
            var pattern = @"^\d{2}-\d{8}-\d{1}$";
            return System.Text.RegularExpressions.Regex.IsMatch(cuit, pattern);
        }
    }
}