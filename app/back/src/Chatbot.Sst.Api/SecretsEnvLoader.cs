namespace Chatbot.Sst.Api;

/// <summary>
/// Loads a repo-root <c>secrets.env</c> (KEY=VALUE lines, <c>#</c> comments) into process
/// environment variables for local Development. The configuration env-var provider then maps
/// <c>Foo__Bar</c> to <c>Foo:Bar</c>, so secrets.env overrides appsettings (e.g. <c>Llm__BaseUrl</c>
/// / <c>Llm__ApiKey</c> pointing the LLM at the Lightning studio) without committing the secret.
/// A variable already present in the real environment is left untouched — explicit wins. Mirrors
/// the Python backend, which also reads secrets.env at startup.
/// </summary>
internal static class SecretsEnvLoader
{
    public static void LoadFromAncestors(string fileName = "secrets.env")
    {
        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(origin); dir is not null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, fileName);
                if (File.Exists(path))
                {
                    Apply(path);
                    return;
                }
            }
        }
    }

    private static void Apply(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            // Split on the first '=' only: values legitimately contain '=' (query strings, DSNs).
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
