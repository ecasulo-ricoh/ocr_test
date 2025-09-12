using DocuWare.Platform.ServerClient;

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
}