using Microsoft.Extensions.Logging;

namespace CSharpMcpService.Services;

public static class EmbeddingServiceFactory
{
    public static IEmbeddingService CreateEmbeddingService(
        ILoggerFactory loggerFactory,
        bool useAdvancedEmbedding = true)
    {
        if (useAdvancedEmbedding)
        {
            // Try to use EmbeddingGemma service first
            try
            {
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var gemmaService = new EmbeddingGemmaService(
                    loggerFactory.CreateLogger<EmbeddingGemmaService>(),
                    httpClient);

                // Test if the service is available
                var testTask = Task.Run(async () =>
                {
                    await gemmaService.InitializeAsync();
                    var testEmbedding = await gemmaService.GenerateEmbeddingAsync("test");
                    return testEmbedding.Length > 0;
                });

                if (testTask.Result) // Synchronously check the result for testing
                {
                    return gemmaService;
                }
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger(typeof(EmbeddingServiceFactory))
                    .LogWarning(ex, "EmbeddingGemma service not available, falling back to simple embedding");
            }
        }

        // Fallback to simple embedding service
        return new SimpleEmbeddingService(
            loggerFactory.CreateLogger<SimpleEmbeddingService>());
    }
}