using DocuWare.Platform.ServerClient;
using OCR_test.Services.Interfaces;

namespace OCR_test.Services.Implementations
{
    /// <summary>
    /// Implementaci�n del servicio de conexi�n con DocuWare
    /// </summary>
    public class DocuWareConnectionService : IDocuWareConnectionService
    {
        private readonly IDocuWareConfigurationService _configService;
        private readonly ILogger<DocuWareConnectionService> _logger;
        private ServiceConnection? _connection;
        private readonly object _lockObject = new object();

        public DocuWareConnectionService(
            IDocuWareConfigurationService configService,
            ILogger<DocuWareConnectionService> logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public ServiceConnection GetConnection()
        {
            if (_connection == null)
            {
                lock (_lockObject)
                {
                    if (_connection == null)
                    {
                        try
                        {
                            var uri = _configService.GetDocuWareUri();
                            var user = _configService.GetUser();
                            var password = _configService.GetPassword();

                            _logger.LogInformation("Estableciendo conexi�n con DocuWare: {Uri}", uri);

                            _connection = ServiceConnection.Create(new Uri(uri), user, password);

                            _logger.LogInformation("Conexi�n con DocuWare establecida exitosamente");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error al establecer conexi�n con DocuWare");
                            throw new InvalidOperationException("No se pudo establecer conexi�n con DocuWare", ex);
                        }
                    }
                }
            }

            return _connection;
        }

        public async Task<Organization[]> GetOrganizationsAsync()
        {
            try
            {
                var connection = GetConnection();
                var organizations = await connection.GetOrganizationsAsync();
                
                _logger.LogInformation("Se obtuvieron {Count} organizaciones", organizations.Length);
                
                return organizations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener organizaciones");
                throw;
            }
        }

        public void Dispose()
        {
            // El ServiceConnection en esta versi�n no implementa IDisposable
            _connection = null;
            _logger.LogInformation("Conexi�n con DocuWare liberada");
        }
    }
}