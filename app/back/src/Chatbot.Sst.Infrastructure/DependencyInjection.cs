using Chatbot.Sst.Application;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Application.Generation;
using Chatbot.Sst.Infrastructure.Dispatch;
using Chatbot.Sst.Infrastructure.Llm;
using Chatbot.Sst.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Chatbot.Sst.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChatbotDispatchOptions>()
            .Bind(configuration.GetSection(ChatbotDispatchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GenerationOptions>()
            .Bind(configuration.GetSection(GenerationOptions.SectionName));

        services.AddHttpClient<ILlmProvider, OpenAiCompatibleLlmProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        services.AddHttpClient<IChatbotDispatchClient, HttpChatbotDispatchClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ChatbotDispatchOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            // The Python side is stdlib http.server.ThreadingHTTPServer, not a real
            // ASGI server — it doesn't reliably support HTTP keep-alive connection
            // reuse under load ("response ended prematurely" on pooled connections
            // it already half-closed). Force a fresh connection per request.
            client.DefaultRequestHeaders.ConnectionClose = true;
        });

        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var config = ConfigurationOptions.Parse(options.Configuration);
            if (!string.IsNullOrEmpty(options.Password))
            {
                config.Password = options.Password;
            }
            // Don't crash startup if Redis is briefly unavailable; the multiplexer reconnects.
            config.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(config);
        });
        services.AddSingleton<IChatEventStream, RedisChatEventStream>();

        services.AddSingleton<IQueryNormalizer, DefaultQueryNormalizer>();
        services.AddSingleton<IChatService, GroundedAnswerService>();
        services.AddSingleton<IChatRequestStore, InMemoryChatRequestStore>();
        services.AddSingleton<IChatDispatchCoordinator>(sp => new ChatDispatchCoordinator(
            sp.GetRequiredService<IChatRequestStore>(),
            sp.GetRequiredService<IChatbotDispatchClient>(),
            sp.GetRequiredService<IChatService>(),
            sp.GetRequiredService<IChatEventStream>(),
            sp.GetRequiredService<IOptions<GenerationOptions>>().Value,
            sp.GetRequiredService<ILogger<ChatDispatchCoordinator>>()));

        return services;
    }
}
