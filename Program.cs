using OCR_test.Services.Interfaces;
using OCR_test.Services.Implementations;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "OCR Test API",
        Version = "v1",
        Description = "API para pruebas de OCR con documentos de DocuWare"
    });

    // Incluir comentarios XML
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Registrar servicios de DocuWare
builder.Services.AddSingleton<IDocuWareConfigurationService, DocuWareConfigurationService>();
builder.Services.AddSingleton<IDocuWareConnectionService, DocuWareConnectionService>();
builder.Services.AddScoped<IDocuWareDocumentService, DocuWareDocumentService>();
builder.Services.AddScoped<IOcrService, OcrService>();

// Configurar logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "OCR Test API v1");
        c.RoutePrefix = string.Empty; // Swagger en la raíz
    });
}

// Solo usar HTTPS redirect si no estamos en desarrollo o si HTTPS está disponible
if (!app.Environment.IsDevelopment() || 
    app.Configuration.GetValue<string>("ASPNETCORE_URLS")?.Contains("https") == true ||
    app.Urls.Any(url => url.StartsWith("https")))
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

// Agregar endpoint de salud básico
app.MapGet("/health", () => "OK").WithName("Health");

// Log de inicio con URLs correctas
app.Logger.LogInformation("?? OCR Test API iniciada correctamente");
app.Logger.LogInformation("?? Swagger disponible en la raíz de la aplicación");

app.Run();
