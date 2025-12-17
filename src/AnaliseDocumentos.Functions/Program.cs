using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Azure;
using Azure.Identity;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure;
using AnaliseDocumentos.Core.Interfaces;
using AnaliseDocumentos.Infrastructure.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core; // Necessário para configurar limite de tamanho

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // 1. AUMENTAR LIMITE DE UPLOAD (Evita erro com PDFs grandes)
        // Configura o Kestrel para aceitar até 100 MB (limite da Azure Function HTTP)
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = 104_857_600; // 100 MB
        });

        // 2. Configuração do Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // 3. Configuração do Blob Storage
        services.AddAzureClients(clientBuilder =>
        {
            // Tenta ler a Connection String (Local) ou a URI do serviço (Produção/Identity)
            var connectionString = configuration["AzureWebJobsStorage"];
            var blobServiceUri = configuration["AzureWebJobsStorage:blobServiceUri"];

            // Prioriza Identity se a URI estiver configurada (Cenário de Produção sem chaves)
            if (!string.IsNullOrEmpty(blobServiceUri))
            {
                clientBuilder.AddBlobServiceClient(new Uri(blobServiceUri));
                clientBuilder.UseCredential(new DefaultAzureCredential());
            }
            // Fallback para Connection String (Cenário Local)
            else if (!string.IsNullOrEmpty(connectionString))
            {
                clientBuilder.AddBlobServiceClient(connectionString);
            }
            else
            {
                throw new InvalidOperationException("Configuração do Storage não encontrada. Verifique 'AzureWebJobsStorage' ou 'AzureWebJobsStorage:blobServiceUri'.");
            }
        });

        // 4. Configuração do Document Intelligence
        var docIntelEndpoint = configuration["DocIntelEndpoint"];
        var docIntelApiKey = configuration["DocIntelApiKey"];

        ArgumentException.ThrowIfNullOrEmpty(docIntelEndpoint, "DocIntelEndpoint");

        if (!string.IsNullOrEmpty(docIntelApiKey))
        {
            services.AddSingleton(new DocumentAnalysisClient(
                new Uri(docIntelEndpoint),
                new AzureKeyCredential(docIntelApiKey)));
        }
        else
        {
            services.AddSingleton(new DocumentAnalysisClient(
                new Uri(docIntelEndpoint),
                new DefaultAzureCredential()));
        }

        // 5. Injeção dos Serviços de Domínio
        services.AddScoped<IServicoOcr, ServicoAzureDocIntel>();
        services.AddScoped<IServicoFoundry, ServicoAgenteFoundry>();
    })
    .Build();

host.Run();