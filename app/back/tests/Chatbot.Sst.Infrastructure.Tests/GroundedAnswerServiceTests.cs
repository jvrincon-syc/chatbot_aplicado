using System.Runtime.CompilerServices;
using System.Text;
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

        public async IAsyncEnumerable<LlmStreamChunk> GenerateStreamingAsync(
            LlmRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Calls++;
            Last = request;
            // Split into two chunks so the test proves deltas concatenate back to the full text.
            var mid = reply.Length / 2;
            yield return new LlmStreamChunk(reply[..mid]);
            yield return new LlmStreamChunk(reply[mid..]);
            await Task.CompletedTask;
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
    public async Task With_evidence_formats_plain_text_and_deduplicates_citations()
    {
        var llm = new SpyLlm("### **Correo**\nEl correo es **convivencia@empresa.com**.\n\nFuentes\n- doc-1");
        var citation = new Citation("doc-1", "Manual SST", "12");
        var evidence = new EvidencePackage(
            [new Evidence("Texto uno", citation, 0.9), new Evidence("Texto dos", citation, 0.8)],
            20);

        var result = await Service(llm).AnswerAsync(new UserQuestion("Â¿CuÃ¡l es el correo?"), evidence, CancellationToken.None);

        Assert.False(result.Abstained);
        Assert.Equal("Correo\nEl correo es convivencia@empresa.com.", result.Answer);
        Assert.Single(result.Citations);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task Streaming_with_evidence_yields_deltas_then_final_with_citations()
    {
        var llm = new SpyLlm("respuesta");
        var evidence = new EvidencePackage(
            [new Evidence("El extintor se revisa cada mes.", new Citation("doc-1", "Manual SST", "12"), 0.9)],
            10);

        var deltas = new StringBuilder();
        ChatResponse? final = null;
        await foreach (var chunk in Service(llm).AnswerStreamingAsync(
            new UserQuestion("¿cada cuánto?"), evidence, CancellationToken.None))
        {
            if (chunk.IsFinal) final = chunk.Final;
            else deltas.Append(chunk.Delta);
        }

        Assert.Equal("respuesta", deltas.ToString());
        Assert.NotNull(final);
        Assert.Equal("respuesta", final!.Answer);
        Assert.False(final.Abstained);
        Assert.Single(final.Citations);
        Assert.Equal("doc-1", final.Citations[0].DocumentId);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task Streaming_empty_evidence_abstains_without_calling_the_llm()
    {
        var llm = new SpyLlm("should not be used");
        ChatResponse? final = null;
        await foreach (var chunk in Service(llm).AnswerStreamingAsync(
            new UserQuestion("¿algo?"), EvidencePackage.Empty, CancellationToken.None))
        {
            if (chunk.IsFinal) final = chunk.Final;
        }

        Assert.NotNull(final);
        Assert.True(final!.Abstained);
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public void Prompt_builder_numbers_sources_and_includes_system_prompt()
    {
        var evidence = new EvidencePackage(
            [new Evidence("texto uno", new Citation("d1", "Doc Uno", "3", "2.1"), 0.5)], 5);
        var messages = EvidencePromptBuilder.Build(new NormalizedQuestion("pregunta"), evidence);

        Assert.Equal(LlmRole.System, messages[0].Role);
        Assert.Contains("only from the supplied evidence", messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warm, clear", messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact data first", messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plain text only", messages[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[SOURCE 1]", messages[1].Content);
        Assert.Contains("Doc Uno", messages[1].Content);
        Assert.Contains("texto uno", messages[1].Content);
    }
}
