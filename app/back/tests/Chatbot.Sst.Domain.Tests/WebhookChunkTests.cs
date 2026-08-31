using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Domain.Tests;

public sealed class WebhookChunkTests
{
    [Fact]
    public void ToEvidence_prefers_citation_label_over_document_name()
    {
        var chunk = new WebhookChunk(
            "node-1",
            "doc_123",
            "Texto",
            0.91,
            "vector",
            Metadata: new Dictionary<string, string?>
            {
                ["citation_label"] = "Politica SST",
                ["document_name"] = "politica_sst.pdf"
            });

        var evidence = chunk.ToEvidence();

        Assert.Equal("doc_123", evidence.Citation.DocumentId);
        Assert.Equal("Politica SST", evidence.Citation.DocumentTitle);
    }

    [Fact]
    public void ToEvidence_falls_back_to_document_name_when_citation_label_is_missing()
    {
        var chunk = new WebhookChunk(
            "node-1",
            "doc_123",
            "Texto",
            0.91,
            "vector",
            Metadata: new Dictionary<string, string?>
            {
                ["document_name"] = "politica_sst.pdf"
            });

        var evidence = chunk.ToEvidence();

        Assert.Equal("politica_sst.pdf", evidence.Citation.DocumentTitle);
    }
}
