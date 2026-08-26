using Chatbot.Sst.Application;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Infrastructure.Llm;
using Chatbot.Sst.Infrastructure.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chatbot.Sst.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // RagTarget — validated on startup, fail closed if any identifier is missing.
        services.AddOptions<RagTargetOptions>()
            .Bind(configuration.GetSection(RagTargetOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IRagTargetProvider, ConfiguredRagTargetProvider>();

        // LLM — typed HttpClient to the local OpenAI-compatible server.
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<ILlmProvider, OpenAiCompatibleLlmProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        // Application use cases (pure orchestration; depend only on Domain + ports).
        services.AddSingleton<IQueryNormalizer, DefaultQueryNormalizer>();
        services.AddSingleton<IChatService, GroundedAnswerService>();

        return services;
    }
}
