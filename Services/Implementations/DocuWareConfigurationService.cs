using OCR_test.Services.Interfaces;

namespace OCR_test.Services.Implementations
{
    /// <summary>
    /// Implementaci�n del servicio de configuraci�n de DocuWare
    /// </summary>
    public class DocuWareConfigurationService : IDocuWareConfigurationService
    {
        private readonly IConfiguration _configuration;

        public DocuWareConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetFileCabinetId()
        {
            return _configuration["DWEnvVariables:CabinetGUID"]
                ?? throw new InvalidOperationException("FileCabinet GUID no configurado en appsettings.json");
        }

        public string GetDocuWareUri()
        {
            return _configuration["DWEnvVariables:Uri"]
                ?? throw new InvalidOperationException("URI de DocuWare no configurada en appsettings.json");
        }

        public string GetUser()
        {
            return _configuration["DWEnvVariables:User"]
                ?? throw new InvalidOperationException("Usuario de DocuWare no configurado en appsettings.json");
        }

        public string GetPassword()
        {
            return _configuration["DWEnvVariables:Password"]
                ?? throw new InvalidOperationException("Contrase�a de DocuWare no configurada en appsettings.json");
        }
    }
}