using Chatbot.Sst.Application;
using Chatbot.Sst.Application.Abstractions;
using Chatbot.Sst.Application.Generation;
using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Infrastructure.Tests;

public class GroundedAnswerServiceTests
{
    private sealed class SpyLlm(string reply) : ILlmProvider
    {
        public int Calls { get; private set; }
        public LlmRequest? Last { get; private set; }

        public Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct)
        {
            Calls++;
            Last = request;
            return Task.FromResult(new LlmResponse(reply));
        }

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);
    }

    private static GroundedAnswerService Service(SpyLlm llm)
        => new(new DefaultQueryNormalizer(), llm);

    [Fact]
    public async Task Empty_evidence_abstains_without_calling_the_llm()
    {
        var llm = new SpyLlm("should not be used");
        var result = await Service(llm).AnswerAsync(new UserQuestion("¿algo?"), EvidencePackage.Empty, CancellationToken.None);

        Assert.True(result.Abstained);
        Assert.Equal(ChatResponse.AbstentionMessage, result.Answer);
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task With_evidence_calls_llm_and_returns_citations()
    {
        var llm = new SpyLlm("respuesta");
        var evidence = new EvidencePackage(
            [new Evidence("El extintor se revisa cada mes.", new Citation("doc-1", "Manual SST", "12"), 0.9)],
            10);

        var result = await Service(llm).AnswerAsync(new UserQuestion("¿cada cuánto?"), evidence, CancellationToken.None);

        Assert.False(result.Abstained);
        Assert.Equal("respuesta", result.Answer);
        Assert.Single(result.Citations);
        Assert.Equal("doc-1", result.Citations[0].DocumentId);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public void Prompt_builder_numbers_sources_and_includes_system_prompt()
    {
        var evidence = new EvidencePackage(
            [new Evidence("texto uno", new Citation("d1", "Doc Uno", "3", "2.1"), 0.5)], 5);
        var messages = EvidencePromptBuilder.Build(new NormalizedQuestion("pregunta"), evidence);

        Assert.Equal(LlmRole.System, messages[0].Role);
        Assert.Contains("only from the supplied evidence", messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[SOURCE 1]", messages[1].Content);
        Assert.Contains("Doc Uno", messages[1].Content);
        Assert.Contains("texto uno", messages[1].Content);
    }
}
