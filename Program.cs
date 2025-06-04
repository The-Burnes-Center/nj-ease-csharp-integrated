using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using DocumentValidator.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<DocumentValidatorService>();
        services.AddSingleton<PdfGeneratorService>();
        services.AddSingleton<ConfigurationService>();
    })
    .Build();

host.Run(); 