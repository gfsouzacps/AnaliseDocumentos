using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using AnaliseDocumentos.Core.Interfaces;
using AnaliseDocumentos.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        // Configuração Document Intelligence com Managed Identity
        var docIntelEndpoint = Environment.GetEnvironmentVariable("DocIntelEndpoint");
        if (!string.IsNullOrEmpty(docIntelEndpoint))
        {
            services.AddSingleton(new DocumentAnalysisClient(
                new Uri(docIntelEndpoint), 
                new DefaultAzureCredential()));
        }

        services.AddScoped<IServicoOcr, ServicoAzureDocIntel>();

        services.AddScoped<IServicoFoundry, ServicoAgenteFoundry>();
    })
    .Build();

host.Run();
