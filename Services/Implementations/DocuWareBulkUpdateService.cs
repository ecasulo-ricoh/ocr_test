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
        private readonly ICsvLoggingService _csvLoggingService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DocuWareBulkUpdateService> _logger;

        // Configuración para límites y valores vacíos
        private readonly int _maxDocumentLimit;
        private readonly List<string> _emptyFieldValues;
        private readonly bool _treatEmptyValuesAsPlaceholders;
        private readonly bool _logPlaceholderReplacements;

        public DocuWareBulkUpdateService(
            IDocuWareConnectionService connectionService,
            IDocuWareConfigurationService configService,
            IInvoiceAnalysisService invoiceAnalysisService,
            ICsvLoggingService csvLoggingService,
            IConfiguration configuration,
            ILogger<DocuWareBulkUpdateService> logger)
        {
            _connectionService = connectionService;
            _configService = configService;
            _invoiceAnalysisService = invoiceAnalysisService;
            _csvLoggingService = csvLoggingService;
            _configuration = configuration;
            _logger = logger;

            // Cargar configuración de límites y valores vacíos
            _maxDocumentLimit = _configuration.GetValue<int>("BulkUpdate:MaxDocumentLimit", 5000);
            _emptyFieldValues = _configuration.GetSection("BulkUpdate:EmptyFieldValues")
                .Get<List<string>>() ?? new List<string> { "--", "", "N/A", "NULL", "null", "undefined", " ", "  ", "   " };
            
            // ? NUEVAS CONFIGURACIONES PARA MANEJO DE PLACEHOLDERS
            _treatEmptyValuesAsPlaceholders = _configuration.GetValue<bool>("BulkUpdate:TreatEmptyValuesAsPlaceholders", true);
            _logPlaceholderReplacements = _configuration.GetValue<bool>("BulkUpdate:LogPlaceholderReplacements", true);

            _logger.LogInformation("?? Configuración bulk update cargada: " +
                "Límite máximo: {MaxLimit}, " +
                "Valores vacíos/placeholder: [{EmptyValues}], " +
                "Tratar como placeholders: {TreatAsPlaceholders}, " +
                "Log reemplazos: {LogReplacements}", 
                _maxDocumentLimit, 
                string.Join(", ", _emptyFieldValues), 
                _treatEmptyValuesAsPlaceholders,
                _logPlaceholderReplacements);
        }

        /// <summary>
        /// Obtiene todos los documentos del file cabinet usando paginación recursiva
        /// </summary>
        public static void GetAllDocuments(DocumentsQueryResult queryResult, List<Document> documents)
        {
            documents.AddRange(queryResult.Items);

            if (queryResult.NextRelationLink != null)
            {
                GetAllDocuments(queryResult.GetDocumentsQueryResultFromNextRelationAsync().Result, documents);
            }
        }

        public async Task<BulkUpdateResultDto> BulkUpdateDocumentsAsync(BulkUpdateInternalRequestDto request)
        {
            var stopwatch = Stopwatch.StartNew();
            var batchId = $"batch_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
            
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

            // Estadísticas para el reporte CSV
            var issueStats = new Dictionary<string, int>
            {
                ["CuitIssues"] = 0,
                ["ValidationIssues"] = 0,
                ["OcrFailures"] = 0,
                ["DocuWareFailures"] = 0,
                ["TipoEDetected"] = 0
            };

            try
            {
                var mode = request.DryRun ? "DRY-RUN" : "ACTUALIZACIÓN REAL";
                var updateStrategy = request.OnlyUpdateEmptyFields ? "SOLO CAMPOS VACÍOS/PLACEHOLDER" : "SOBRESCRIBIR TODOS";
                
                // ? VALIDAR LÍMITE MÁXIMO DE DOCUMENTOS
                if (request.DocumentCount > _maxDocumentLimit)
                {
                    var errorMessage = $"Límite excedido: Se solicitaron {request.DocumentCount} documentos, pero el máximo permitido es {_maxDocumentLimit}";
                    _logger.LogWarning("?? {ErrorMessage}", errorMessage);
                    
                    result.Success = false;
                    result.Message = errorMessage;
                    
                    await _csvLoggingService.LogBatchSummaryAsync(batchId, 0, 0, 0, 0, issueStats);
                    return result;
                }
                
                _logger.LogInformation("?? Iniciando procesamiento on-the-fly [{BatchId}] en modo {Mode} con estrategia {Strategy}. Documentos objetivo: {Count}/{MaxLimit}", 
                    batchId, mode, updateStrategy, request.DocumentCount, _maxDocumentLimit);

                var ocrTimes = new List<long>();
                var updateTimes = new List<long>();

                // ?? PROCESAMIENTO ON-THE-FLY: Buscar y procesar inmediatamente
                await ProcessDocumentsOnTheFlyAsync(
                    result.Metadata.FileCabinetId, 
                    request.DocumentCount,
                    request.DryRun,
                    request.OnlyUpdateEmptyFields,
                    request.Language,
                    batchId,
                    result,
                    issueStats,
                    ocrTimes,
                    updateTimes);

                stopwatch.Stop();

                // Calcular estadísticas de rendimiento
                result.Metadata.EndTime = DateTime.UtcNow;
                result.Metadata.TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                result.Metadata.Performance = new PerformanceStatsDto
                {
                    AverageOcrTimeMs = ocrTimes.Any() ? ocrTimes.Average() : 0,
                    AverageUpdateTimeMs = updateTimes.Any() ? updateTimes.Average() : 0,
                    DocumentsPerSecond = result.TotalProcessed / Math.Max(stopwatch.ElapsedMilliseconds / 1000.0, 0.001),
                    TotalOcrTimeMs = ocrTimes.Sum(),
                    TotalUpdateTimeMs = updateTimes.Sum()
                };

                result.Success = true;
                result.Message = $"Procesamiento on-the-fly completado en modo {mode} con estrategia {updateStrategy}. " +
                    $"Procesados: {result.TotalProcessed}, Modificados: {result.SuccessfulUpdates}, " +
                    $"Errores: {result.FailedUpdates}, Sin cambios: {result.SkippedDocuments}";

                // Registrar resumen del batch en CSV
                await _csvLoggingService.LogBatchSummaryAsync(batchId, result.TotalProcessed, 
                    result.SuccessfulUpdates, result.FailedUpdates, result.SkippedDocuments, issueStats);

                _logger.LogInformation("?? Procesamiento on-the-fly completado en {ElapsedMs}ms. {Message}",
                    stopwatch.ElapsedMilliseconds, result.Message);
                    
                _logger.LogInformation("?? Estadísticas CSV: Errores OCR: {OCR}, Validación: {Val}, DocuWare: {DW}, CUIT: {CUIT}, Tipo E: {TipoE}",
                    issueStats["OcrFailures"], issueStats["ValidationIssues"], issueStats["DocuWareFailures"], 
                    issueStats["CuitIssues"], issueStats["TipoEDetected"]);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error en procesamiento on-the-fly");

                result.Success = false;
                result.Message = $"Error en procesamiento on-the-fly: {ex.Message}";
                result.Errors.Add(ex.Message);
                result.Metadata.EndTime = DateTime.UtcNow;
                result.Metadata.TotalProcessingTimeMs = stopwatch.ElapsedMilliseconds;

                // Registrar batch fallido
                await _csvLoggingService.LogBatchSummaryAsync(batchId, result.TotalProcessed, 
                    result.SuccessfulUpdates, result.FailedUpdates, result.SkippedDocuments, issueStats);

                return result;
            }
        }

        /// <summary>
        /// ?? PROCESAMIENTO ON-THE-FLY: Busca y procesa documentos inmediatamente conforme los encuentra
        /// </summary>
        private async Task ProcessDocumentsOnTheFlyAsync(
            string fileCabinetId,
            int targetDocumentCount,
            bool dryRun,
            bool onlyUpdateEmptyFields,
            string language,
            string batchId,
            BulkUpdateResultDto result,
            Dictionary<string, int> issueStats,
            List<long> ocrTimes,
            List<long> updateTimes)
        {
            var connection = _connectionService.GetConnection();
            var documentsFound = 0;
            var startId = 1;
            var maxSearchAttempts = targetDocumentCount * 20; // Buscar hasta 20x más para encontrar documentos reales
            
            _logger.LogInformation("?? Iniciando búsqueda on-the-fly de {Target} documentos desde ID {StartId}...", 
                targetDocumentCount, startId);

            try
            {
                // ?? Búsqueda y procesamiento on-the-fly
                for (int testId = startId; testId <= startId + maxSearchAttempts && documentsFound < targetDocumentCount; testId++)
                {
                    try
                    {
                        // 1. Intentar obtener el documento para verificar si existe
                        var documentResponse = await connection.GetFromDocumentForDocumentAsync(testId, fileCabinetId);
                        
                        if (documentResponse?.Content != null)
                        {
                            var document = documentResponse.Content.GetDocumentFromSelfRelation();
                            if (document != null)
                            {
                                documentsFound++;
                                _logger.LogInformation("? Documento {Found}/{Target} encontrado: ID {DocumentId} - Procesando inmediatamente...", 
                                    documentsFound, targetDocumentCount, document.Id);

                                // ?? 2. PROCESAR INMEDIATAMENTE el documento encontrado
                                await ProcessSingleDocumentAsync(
                                    document.Id, 
                                    documentsFound, 
                                    targetDocumentCount,
                                    fileCabinetId,
                                    dryRun,
                                    onlyUpdateEmptyFields,
                                    language,
                                    result,
                                    issueStats,
                                    ocrTimes,
                                    updateTimes);

                                // Log de progreso cada 10 documentos
                                if (documentsFound % 10 == 0 || documentsFound == targetDocumentCount)
                                {
                                    _logger.LogInformation("?? Progreso on-the-fly: {Current}/{Target} documentos procesados. " +
                                        "Modificados: {Modified}, Errores: {Failed}, Sin cambios: {Skipped}",
                                        documentsFound, targetDocumentCount, result.SuccessfulUpdates, result.FailedUpdates, result.SkippedDocuments);
                                }

                                // 3. Si ya encontramos suficientes, terminar
                                if (documentsFound >= targetDocumentCount)
                                {
                                    _logger.LogInformation("?? Objetivo alcanzado: {Found} documentos procesados on-the-fly", documentsFound);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // El documento no existe, continuar buscando sin hacer ruido en logs
                        continue;
                    }

                    // Log de progreso de búsqueda cada 100 intentos
                    if (testId % 100 == 0)
                    {
                        _logger.LogDebug("?? Búsqueda en progreso: Revisado hasta ID {TestId}, encontrados {Found}/{Target}", 
                            testId, documentsFound, targetDocumentCount);
                    }
                }

                if (documentsFound == 0)
                {
                    _logger.LogWarning("?? No se encontraron documentos en el FileCabinet después de {Attempts} intentos", maxSearchAttempts);
                    throw new InvalidOperationException($"No se encontraron documentos en el FileCabinet {fileCabinetId}");
                }
                else if (documentsFound < targetDocumentCount)
                {
                    _logger.LogWarning("?? Solo se encontraron {Found} documentos de los {Target} solicitados después de {Attempts} intentos", 
                        documentsFound, targetDocumentCount, maxSearchAttempts);
                }

                _logger.LogInformation("? Búsqueda on-the-fly completada: {Found} documentos encontrados y procesados", documentsFound);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error en procesamiento on-the-fly");
                throw;
            }
        }

        /// <summary>
        /// ?? Procesa un solo documento inmediatamente después de encontrarlo
        /// </summary>
        private async Task ProcessSingleDocumentAsync(
            int documentId,
            int currentIndex,
            int totalTarget,
            string fileCabinetId,
            bool dryRun,
            bool onlyUpdateEmptyFields,
            string language,
            BulkUpdateResultDto result,
            Dictionary<string, int> issueStats,
            List<long> ocrTimes,
            List<long> updateTimes)
        {
            var docStopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("?? Procesando documento {Current}/{Total}: ID {DocumentId}", 
                    currentIndex, totalTarget, documentId);

                // 1. Ejecutar OCR en el documento
                var ocrStopwatch = Stopwatch.StartNew();
                var ocrResult = await _invoiceAnalysisService.AnalyzeInvoiceSimplifiedAsync(
                    documentId, fileCabinetId, language);
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
                    result.Errors.Add($"? Documento {documentId}: Error en OCR");
                    
                    // Registrar fallo de OCR en CSV
                    await _csvLoggingService.LogOcrFailureAsync(documentId, ocrResult.Message ?? "Error desconocido en OCR");
                    issueStats["OcrFailures"]++;
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

                    // Registrar estadísticas especiales
                    if (ocrResult.Data.TipoFactura == "E")
                    {
                        issueStats["TipoEDetected"]++;
                        _logger.LogInformation("? Detectado tipo de factura E (nuevo tipo) en documento {DocumentId}", documentId);
                    }

                    // 3. Validar y preparar campos para actualización
                    var validationResult = await ValidateAndPrepareFieldsAsync(
                        documentId, ocrResult.Data, fileCabinetId, onlyUpdateEmptyFields);
                    
                    detail.UpdatedFields = validationResult.ValidatedFields;
                    detail.SkippedFields = validationResult.SkippedFields;
                    detail.ValidationWarnings = validationResult.ValidationWarnings;

                    // Registrar problemas de validación en CSV
                    if (validationResult.ValidationWarnings.Any())
                    {
                        issueStats["ValidationIssues"]++;
                        foreach (var warning in validationResult.ValidationWarnings)
                        {
                            // Determinar qué campo causó el problema
                            var fieldName = "Unknown";
                            var detectedValue = "Unknown";
                            
                            if (warning.Contains("LETRA") || warning.Contains("LETRA_DOCUMENTO"))
                            {
                                fieldName = "LETRA";
                                detectedValue = ocrResult.Data.TipoFactura ?? "";
                            }
                            else if (warning.Contains("CODIGO") || warning.Contains("CODIGO_DOCUMENTO"))
                            {
                                fieldName = "CODIGO";
                                detectedValue = ocrResult.Data.CodigoFactura ?? "";
                            }
                            else if (warning.Contains("CUIT_CLIENTE"))
                            {
                                fieldName = "CUIT_CLIENTE";
                                detectedValue = ocrResult.Data.CuitCliente ?? "";
                                
                                // Caso especial para problemas de CUIT
                                await _csvLoggingService.LogCuitIssueAsync(documentId, detectedValue, 
                                    "Validation Error", warning);
                                issueStats["CuitIssues"]++;
                            }
                            
                            await _csvLoggingService.LogValidationIssueAsync(documentId, fieldName, 
                                detectedValue, warning);
                        }
                    }

                    // 4. Verificar si hay campos para actualizar
                    if (HasFieldsToUpdate(validationResult.ValidatedFields))
                    {
                        // 5. Actualizar documento en DocuWare
                        var updateStopwatch = Stopwatch.StartNew();
                        var updateResult = await UpdateDocumentFieldsAsync(
                            documentId, validationResult.ValidatedFields, fileCabinetId, dryRun);
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
                            result.Errors.Add($"? Documento {documentId}: Error en actualización");
                            
                            // Registrar fallo de DocuWare en CSV
                            await _csvLoggingService.LogDocuWareUpdateFailureAsync(documentId, 
                                updateResult.Message, SerializeFields(validationResult.ValidatedFields));
                            issueStats["DocuWareFailures"]++;
                        }
                        else
                        {
                            result.SkippedDocuments++;
                            _logger.LogInformation("?? Documento {DocumentId} omitido: {Message}", documentId, updateResult.Message);
                        }
                    }
                    else
                    {
                        detail.Status = "NoChanges";
                        detail.Message = "No se encontraron campos válidos para actualizar después de validaciones";
                        result.SkippedDocuments++;
                        _logger.LogInformation("?? Documento {DocumentId} sin cambios: no hay campos para actualizar", documentId);
                    }
                }

                docStopwatch.Stop();
                detail.ProcessingTimeMs = docStopwatch.ElapsedMilliseconds;
                result.Details.Add(detail);
                result.TotalProcessed++;
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
                result.Errors.Add($"? Documento {documentId}: Error inesperado");
                
                // Registrar error inesperado
                await _csvLoggingService.LogOcrFailureAsync(documentId, $"Error inesperado: {ex.Message}");
                issueStats["OcrFailures"]++;
            }
        }

        /// <summary>
        /// ?? LEGACY: Obtiene lista de documentos (usado solo para endpoint de información)
        /// </summary>
        public async Task<List<int>> GetDocumentListAsync(string fileCabinetId, int count)
        {
            try
            {
                _logger.LogInformation("?? LEGACY: Obteniendo lista de {Count} documentos del FileCabinet {FileCabinetId} (solo para información)", 
                    count, fileCabinetId);

                var connection = _connectionService.GetConnection();
                var realDocumentIds = new List<int>();
                var documentsFound = 0;
                var startId = 1;
                var maxAttempts = Math.Min(count * 10, 1000);
                
                for (int testId = startId; testId <= startId + maxAttempts && documentsFound < count; testId++)
                {
                    try
                    {
                        var documentResponse = await connection.GetFromDocumentForDocumentAsync(testId, fileCabinetId);
                        
                        if (documentResponse?.Content != null)
                        {
                            var document = documentResponse.Content.GetDocumentFromSelfRelation();
                            if (document != null)
                            {
                                realDocumentIds.Add(document.Id);
                                documentsFound++;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    
                    if (documentsFound >= count) break;
                }
                
                if (realDocumentIds.Any())
                {
                    return realDocumentIds;
                }
                else
                {
                    // Generar IDs de ejemplo para endpoint informativo
                    var exampleIds = new List<int>();
                    var random = new Random();
                    var baseIds = new[] { 100, 200, 300, 500, 1000, 1500, 2000 };
                    
                    for (int i = 0; i < Math.Min(count, 20); i++)
                    {
                        var baseId = baseIds[i % baseIds.Length];
                        var randomOffset = random.Next(1, 50);
                        exampleIds.Add(baseId + randomOffset);
                    }
                    
                    return exampleIds;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error obteniendo lista de documentos del FileCabinet {FileCabinetId}", fileCabinetId);
                throw new InvalidOperationException($"No se pudieron obtener documentos del FileCabinet {fileCabinetId}: {ex.Message}", ex);
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
                var fieldsToUpdateCount = CountValidFields(fields);
                
                if (fieldsToUpdateCount == 0)
                {
                    result.Status = "NoChanges";
                    result.Message = "No hay campos válidos para actualizar";
                    stopwatch.Stop();
                    result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
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
                var documentResponse = await connection.GetFromDocumentForDocumentAsync(documentId, fileCabinetId);
                var document = documentResponse.Content.GetDocumentFromSelfRelation();

                // ? USAR NOMBRES CORRECTOS DE CAMPOS BIMBO
                var fieldsToUpdate = new List<DocumentIndexField>();

                if (!string.IsNullOrEmpty(fields.LETRA_DOCUMENTO))
                {
                    fieldsToUpdate.Add(DocumentIndexField.Create("LETRA", fields.LETRA_DOCUMENTO));
                    _logger.LogInformation("?? Preparando campo LETRA: {Value}", fields.LETRA_DOCUMENTO);
                }

                if (!string.IsNullOrEmpty(fields.CODIGO_DOCUMENTO))
                {
                    fieldsToUpdate.Add(DocumentIndexField.Create("CODIGO", fields.CODIGO_DOCUMENTO));
                    _logger.LogInformation("?? Preparando campo CODIGO: {Value}", fields.CODIGO_DOCUMENTO);
                }

                if (!string.IsNullOrEmpty(fields.NDEG_FACTURA))
                {
                    fieldsToUpdate.Add(DocumentIndexField.Create("NDEG_FACTURA", fields.NDEG_FACTURA));
                    _logger.LogInformation("?? Preparando campo NDEG_FACTURA: {Value}", fields.NDEG_FACTURA);
                }

                if (!string.IsNullOrEmpty(fields.DATE))
                {
                    if (DateTime.TryParseExact(fields.DATE, "dd/MM/yyyy", 
                        System.Globalization.CultureInfo.InvariantCulture, 
                        System.Globalization.DateTimeStyles.None, out var parsedDate))
                    {
                        fieldsToUpdate.Add(DocumentIndexField.CreateDate("DATE", parsedDate));
                        _logger.LogInformation("?? Preparando campo DATE: {Value} -> {ParsedDate}", fields.DATE, parsedDate);
                    }
                    else
                    {
                        fieldsToUpdate.Add(DocumentIndexField.Create("DATE", fields.DATE));
                        _logger.LogWarning("?? Campo DATE no se pudo parsear como fecha, guardando como string: {Value}", fields.DATE);
                    }
                }

                if (!string.IsNullOrEmpty(fields.CUIT_CLIENTE))
                {
                    fieldsToUpdate.Add(DocumentIndexField.Create("CUIT_CLIENTE", fields.CUIT_CLIENTE));
                    _logger.LogInformation("?? Preparando campo CUIT_CLIENTE: {Value}", fields.CUIT_CLIENTE);
                }

                if (fieldsToUpdate.Any())
                {
                    var fieldsUpdate = new DocumentIndexFields
                    {
                        Field = fieldsToUpdate
                    };

                    _logger.LogInformation("?? Actualizando documento {DocumentId} con {Count} campos en DocuWare...",
                        documentId, fieldsToUpdate.Count);

                    var updateResult = await document.PutToFieldsRelationForDocumentIndexFieldsAsync(fieldsUpdate);

                    _logger.LogInformation("? Documento {DocumentId} actualizado exitosamente en DocuWare con {Count} campos",
                        documentId, fieldsToUpdate.Count);

                    result.Status = "Success";
                    result.Message = $"Documento actualizado exitosamente con {fieldsToUpdate.Count} campos en DocuWare";
                }
                else
                {
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

        #region Métodos de validación y utilidad

        private bool IsValidInvoiceType(string invoiceType)
        {
            return invoiceType == "A" || invoiceType == "B" || invoiceType == "E";
        }

        private bool IsValidInvoiceCode(string invoiceCode)
        {
            return invoiceCode == "001" || invoiceCode == "006" || invoiceCode == "019";
        }

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
                fieldsList.Add($"LETRA={fields.LETRA_DOCUMENTO}");
            
            if (!string.IsNullOrEmpty(fields.CODIGO_DOCUMENTO))
                fieldsList.Add($"CODIGO={fields.CODIGO_DOCUMENTO}");
            
            if (!string.IsNullOrEmpty(fields.NDEG_FACTURA))
                fieldsList.Add($"NDEG_FACTURA={fields.NDEG_FACTURA}");
            
            if (!string.IsNullOrEmpty(fields.DATE))
                fieldsList.Add($"DATE={fields.DATE}");
            
            if (!string.IsNullOrEmpty(fields.CUIT_CLIENTE))
                fieldsList.Add($"CUIT_CLIENTE={fields.CUIT_CLIENTE}");

            return string.Join(", ", fieldsList);
        }

        private bool IsValidInvoiceNumber(string invoiceNumber)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber))
                return false;

            // ? PATRÓN MEJORADO PARA NÚMEROS COMO 00723-0019175
            var pattern = @"^\d{5}-\d{7,8}$";
            return System.Text.RegularExpressions.Regex.IsMatch(invoiceNumber, pattern);
        }

        private bool IsValidDate(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return false;

            return DateTime.TryParseExact(dateString, "dd/MM/yyyy", 
                System.Globalization.CultureInfo.InvariantCulture, 
                System.Globalization.DateTimeStyles.None, out _);
        }

        private bool IsValidCuit(string cuit)
        {
            if (string.IsNullOrWhiteSpace(cuit))
                return false;

            var pattern = @"^\d{2}-\d{8}-\d{1}$";
            return System.Text.RegularExpressions.Regex.IsMatch(cuit, pattern);
        }

        #endregion

        #region Métodos de validación de campos

        private class FieldValidationResult
        {
            public DocumentUpdateFieldsDto ValidatedFields { get; set; } = new();
            public List<string> SkippedFields { get; set; } = new();
            public List<string> ValidationWarnings { get; set; } = new();
        }

        private async Task<FieldValidationResult> ValidateAndPrepareFieldsAsync(
            int documentId, 
            SimplifiedInvoiceDataDto ocrData, 
            string fileCabinetId, 
            bool onlyUpdateEmptyFields)
        {
            var result = new FieldValidationResult();
            var validatedFields = new DocumentUpdateFieldsDto();

            try
            {
                Dictionary<string, string?> currentFields = new();
                
                if (onlyUpdateEmptyFields)
                {
                    currentFields = await GetCurrentDocumentFieldsAsync(documentId, fileCabinetId);
                }

                // ? VALIDAR LETRA (campo LETRA en DocuWare)
                if (!string.IsNullOrEmpty(ocrData.TipoFactura))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "LETRA"))
                    {
                        // ? Log si se detectó un valor placeholder
                        if (onlyUpdateEmptyFields && currentFields.ContainsKey("LETRA") && _logPlaceholderReplacements)
                        {
                            var currentValue = currentFields["LETRA"];
                            if (!string.IsNullOrWhiteSpace(currentValue) && _emptyFieldValues.Any(ev => 
                                string.Equals(ev, currentValue.Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("?? LETRA: Reemplazando valor placeholder '{OldValue}' por '{NewValue}' en documento {DocumentId}", 
                                    currentValue, ocrData.TipoFactura, documentId);
                            }
                        }

                        if (IsValidInvoiceType(ocrData.TipoFactura))
                        {
                            validatedFields.LETRA_DOCUMENTO = ocrData.TipoFactura;
                            _logger.LogInformation("? LETRA validado: {Value} para documento {DocumentId}", 
                                ocrData.TipoFactura, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("LETRA");
                            result.ValidationWarnings.Add($"LETRA inválido: '{ocrData.TipoFactura}' (solo se permiten A, B o E)");
                            _logger.LogWarning("?? LETRA inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.TipoFactura);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("LETRA");
                        result.ValidationWarnings.Add("LETRA omitido: campo ya tiene valor real y onlyUpdateEmptyFields=true");
                        _logger.LogInformation("?? LETRA omitido para documento {DocumentId}: campo tiene valor real '{Value}'", 
                            documentId, currentFields.GetValueOrDefault("LETRA", ""));
                    }
                }

                // ? VALIDAR CODIGO (campo CODIGO en DocuWare)
                if (!string.IsNullOrEmpty(ocrData.CodigoFactura))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "CODIGO"))
                    {
                        // ? Log si se detectó un valor placeholder
                        if (onlyUpdateEmptyFields && currentFields.ContainsKey("CODIGO") && _logPlaceholderReplacements)
                        {
                            var currentValue = currentFields["CODIGO"];
                            if (!string.IsNullOrWhiteSpace(currentValue) && _emptyFieldValues.Any(ev => 
                                string.Equals(ev, currentValue.Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("?? CODIGO: Reemplazando valor placeholder '{OldValue}' por '{NewValue}' en documento {DocumentId}", 
                                    currentValue, ocrData.CodigoFactura, documentId);
                            }
                        }

                        if (IsValidInvoiceCode(ocrData.CodigoFactura))
                        {
                            validatedFields.CODIGO_DOCUMENTO = ocrData.CodigoFactura;
                            _logger.LogInformation("? CODIGO validado: {Value} para documento {DocumentId}", 
                                ocrData.CodigoFactura, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("CODIGO");
                            result.ValidationWarnings.Add($"CODIGO inválido: '{ocrData.CodigoFactura}' (solo se permiten 001, 006 o 019)");
                            _logger.LogWarning("?? CODIGO inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.CodigoFactura);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("CODIGO");
                        result.ValidationWarnings.Add("CODIGO omitido: campo ya tiene valor real y onlyUpdateEmptyFields=true");
                        _logger.LogInformation("?? CODIGO omitido para documento {DocumentId}: campo tiene valor real '{Value}'", 
                            documentId, currentFields.GetValueOrDefault("CODIGO", ""));
                    }
                }

                // ? VALIDAR OTROS CAMPOS CON MEJORES LOGS DE PLACEHOLDER
                if (!string.IsNullOrEmpty(ocrData.NroFactura))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "NDEG_FACTURA"))
                    {
                        if (onlyUpdateEmptyFields && currentFields.ContainsKey("NDEG_FACTURA") && _logPlaceholderReplacements)
                        {
                            var currentValue = currentFields["NDEG_FACTURA"];
                            if (!string.IsNullOrWhiteSpace(currentValue) && _emptyFieldValues.Any(ev => 
                                string.Equals(ev, currentValue.Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("?? NDEG_FACTURA: Reemplazando valor placeholder '{OldValue}' por '{NewValue}' en documento {DocumentId}", 
                                    currentValue, ocrData.NroFactura, documentId);
                            }
                        }

                        if (IsValidInvoiceNumber(ocrData.NroFactura))
                        {
                            validatedFields.NDEG_FACTURA = ocrData.NroFactura;
                            _logger.LogInformation("? NDEG_FACTURA validado: {Value} para documento {DocumentId}", 
                                ocrData.NroFactura, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("NDEG_FACTURA");
                            result.ValidationWarnings.Add($"NDEG_FACTURA formato inválido: '{ocrData.NroFactura}'");
                            _logger.LogWarning("?? NDEG_FACTURA formato inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.NroFactura);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("NDEG_FACTURA");
                        result.ValidationWarnings.Add("NDEG_FACTURA omitido: campo ya tiene valor real y onlyUpdateEmptyFields=true");
                    }
                }

                if (!string.IsNullOrEmpty(ocrData.FechaFactura))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "DATE"))
                    {
                        if (onlyUpdateEmptyFields && currentFields.ContainsKey("DATE") && _logPlaceholderReplacements)
                        {
                            var currentValue = currentFields["DATE"];
                            if (!string.IsNullOrWhiteSpace(currentValue) && _emptyFieldValues.Any(ev => 
                                string.Equals(ev, currentValue.Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("?? DATE: Reemplazando valor placeholder '{OldValue}' por '{NewValue}' en documento {DocumentId}", 
                                    currentValue, ocrData.FechaFactura, documentId);
                            }
                        }

                        if (IsValidDate(ocrData.FechaFactura))
                        {
                            validatedFields.DATE = ocrData.FechaFactura;
                            _logger.LogInformation("? DATE validado: {Value} para documento {DocumentId}", 
                                ocrData.FechaFactura, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("DATE");
                            result.ValidationWarnings.Add($"DATE formato inválido: '{ocrData.FechaFactura}'");
                            _logger.LogWarning("?? DATE formato inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.FechaFactura);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("DATE");
                        result.ValidationWarnings.Add("DATE omitido: campo ya tiene valor real y onlyUpdateEmptyFields=true");
                    }
                }

                if (!string.IsNullOrEmpty(ocrData.CuitCliente))
                {
                    if (!onlyUpdateEmptyFields || IsFieldEmpty(currentFields, "CUIT_CLIENTE"))
                    {
                        if (onlyUpdateEmptyFields && currentFields.ContainsKey("CUIT_CLIENTE") && _logPlaceholderReplacements)
                        {
                            var currentValue = currentFields["CUIT_CLIENTE"];
                            if (!string.IsNullOrWhiteSpace(currentValue) && _emptyFieldValues.Any(ev => 
                                string.Equals(ev, currentValue.Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("?? CUIT_CLIENTE: Reemplazando valor placeholder '{OldValue}' por '{NewValue}' en documento {DocumentId}", 
                                    currentValue, ocrData.CuitCliente, documentId);
                            }
                        }

                        if (IsValidCuit(ocrData.CuitCliente))
                        {
                            validatedFields.CUIT_CLIENTE = ocrData.CuitCliente;
                            _logger.LogInformation("? CUIT_CLIENTE validado: {Value} para documento {DocumentId}", 
                                ocrData.CuitCliente, documentId);
                        }
                        else
                        {
                            result.SkippedFields.Add("CUIT_CLIENTE");
                            result.ValidationWarnings.Add($"CUIT_CLIENTE formato inválido: '{ocrData.CuitCliente}'");
                            _logger.LogWarning("?? CUIT_CLIENTE formato inválido para documento {DocumentId}: {Value}", 
                                documentId, ocrData.CuitCliente);
                        }
                    }
                    else
                    {
                        result.SkippedFields.Add("CUIT_CLIENTE");
                        result.ValidationWarnings.Add("CUIT_CLIENTE omitido: campo ya tiene valor real y onlyUpdateEmptyFields=true");
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron obtener campos actuales para documento {DocumentId}", documentId);
            }

            return fields;
        }

        private bool IsFieldEmpty(Dictionary<string, string?> currentFields, string fieldName)
        {
            if (!currentFields.ContainsKey(fieldName))
            {
                if (_logPlaceholderReplacements)
                {
                    _logger.LogDebug("?? Campo {FieldName} no existe en el documento - se considerará vacío", fieldName);
                }
                return true;
            }

            var value = currentFields[fieldName];
            
            // Null o realmente vacío
            if (string.IsNullOrWhiteSpace(value))
            {
                if (_logPlaceholderReplacements)
                {
                    _logger.LogDebug("?? Campo {FieldName} es null o vacío - se considerará vacío", fieldName);
                }
                return true;
            }

            // ? LÓGICA CONFIGURABLE: Verificar si el valor está en la lista de valores considerados "vacíos"
            if (_treatEmptyValuesAsPlaceholders)
            {
                var trimmedValue = value.Trim();
                
                // Buscar coincidencia exacta (case-sensitive)
                if (_emptyFieldValues.Contains(trimmedValue))
                {
                    if (_logPlaceholderReplacements)
                    {
                        _logger.LogInformation("?? Campo {FieldName} contiene valor placeholder '{Value}' - se considerará vacío para actualización", 
                            fieldName, trimmedValue);
                    }
                    return true;
                }
                
                // Buscar coincidencia insensible a mayúsculas/minúsculas
                if (_emptyFieldValues.Any(ev => string.Equals(ev, trimmedValue, StringComparison.OrdinalIgnoreCase)))
                {
                    if (_logPlaceholderReplacements)
                    {
                        _logger.LogInformation("?? Campo {FieldName} contiene valor placeholder '{Value}' (case-insensitive) - se considerará vacío para actualización", 
                            fieldName, trimmedValue);
                    }
                    return true;
                }
            }

            // El campo tiene un valor real
            if (_logPlaceholderReplacements)
            {
                _logger.LogDebug("?? Campo {FieldName} tiene valor real '{Value}' - NO se actualizará en modo OnlyUpdateEmptyFields", 
                    fieldName, value?.Length > 50 ? value.Substring(0, 50) + "..." : value);
            }
            return false;
        }

        #endregion
    }
}