using Chatbot.Sst.Domain;

namespace Chatbot.Sst.Domain.Tests;

public class RagTargetTests
{
    [Fact]
    public void Targets_with_same_identifiers_are_equal()
    {
        var a = new RagTarget("p", "v", "r");
        var b = new RagTarget("p", "v", "r");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Targets_differing_by_release_are_not_equal()
    {
        var a = new RagTarget("p", "v", "r1");
        var b = new RagTarget("p", "v", "r2");
        Assert.NotEqual(a, b);
    }
}
