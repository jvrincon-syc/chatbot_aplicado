using System.ComponentModel.DataAnnotations;
using Chatbot.Sst.Infrastructure.Rag;

namespace Chatbot.Sst.Infrastructure.Tests;

public class RagTargetOptionsTests
{
    private static IReadOnlyList<ValidationResult> Validate(RagTargetOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Missing_identifiers_fail_validation()
    {
        // Empty defaults must fail closed.
        Assert.NotEmpty(Validate(new RagTargetOptions()));
    }

    [Fact]
    public void Complete_identifiers_pass_validation()
    {
        var options = new RagTargetOptions { ProjectId = "p", RagVariantId = "v", RagReleaseId = "r" };
        Assert.Empty(Validate(options));
    }
}
