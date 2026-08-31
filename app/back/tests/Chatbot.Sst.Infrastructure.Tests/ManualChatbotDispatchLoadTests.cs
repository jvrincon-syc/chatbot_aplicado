using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Chatbot.Sst.Domain;
using Chatbot.Sst.Infrastructure.Dispatch;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Chatbot.Sst.Infrastructure.Tests;

[Trait("Category", "ManualIntegration")]
public sealed class ManualChatbotDispatchLoadTests
{
    private const int TopK = 5;
    private const int MaxConsecutiveFailures = 3;
    // Must exceed ChatbotDispatch.RequestTimeoutSeconds (210s in appsettings.Development.json).
    // SubmitAsync now dispatches in the background and returns immediately, so this polling
    // loop -- not the POST call -- is what actually waits out the full retrieval+LLM chain.
    private static readonly TimeSpan PollingTimeout = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ITestOutputHelper _output;

    private static readonly string[] SstHybridQuestions =
    [
        "Que establece la politica de seguridad y salud en el trabajo?",
        "Cuales son los objetivos del SG-SST?",
        "Como identifica la empresa los peligros y valora los riesgos?",
        "Que programas conforman la planificacion del SG-SST?",
        "Como se gestionan los requisitos legales en SST?",
        "Que contempla la gestion del cambio en seguridad y salud?",
        "Como se prepara la empresa para emergencias?",
        "Que lineamientos aplican a proveedores y contratistas en SST?",
        "Como se hacen auditorias internas del SG-SST?",
        "Como se revisa el SG-SST por la alta direccion?",
        "Que fuentes se usan para identificar oportunidades de mejora continua?",
        "Como se gestionan las acciones correctivas y preventivas?",
        "Como se investigan incidentes accidentes y enfermedades laborales?",
        "Que debe comunicarse al COPASST sobre investigaciones de accidentes?",
        "Que responsabilidades tiene la ARL en seguridad y salud en el trabajo?",
        "Que responsabilidades de SST tiene la organizacion?",
        "Como funciona la induccion y capacitacion anual en SST?",
        "Cuales son las funciones del COPASST?",
        "Que funciones tiene el presidente del COPASST?",
        "Que funciones tiene la secretaria del COPASST?",
        "Como se puede comunicar un trabajador con el COPASST?",
        "Quienes son los miembros principales y suplentes del COPASST 2025 a 2027?",
        "Quien fue nombrado presidente y secretaria del COPASST?",
        "Que es el comite de convivencia laboral?",
        "Cuales son las funciones del comite de convivencia?",
        "Cual es el objetivo del reglamento del comite de convivencia?",
        "Como se conforma el comite de convivencia laboral?",
        "Como funcionan las reuniones del comite de convivencia?",
        "Que metodologia siguen las sesiones del comite de convivencia?",
        "Como se presentan quejas o denuncias de convivencia?",
        "A que correo se envian las quejas de convivencia laboral?",
        "Que derechos tienen los trabajadores en convivencia laboral?",
        "Que deberes de convivencia laboral deben cumplir los trabajadores?",
        "Que principios y valores orientan la convivencia laboral?",
        "En que consiste la politica de desconexion laboral?",
        "Que normas de convivencia deben cumplir los trabajadores?",
        "Que marco legal soporta el comite y la convivencia laboral?",
        "Que dice la politica de prevencion del acoso laboral?",
        "Que es la sala amiga de la familia lactante?",
        "Cuales son las ventajas de la sala amiga?",
        "Donde esta ubicada la sala amiga y quienes pueden usarla?",
        "Como solicito o pido vacaciones?",
        "Que tipos de faltas contempla el reglamento interno de trabajo?",
        "Que sanciones aplican por consumo de alcohol o sustancias psicoactivas?",
        "Que dice la politica de prevencion de alcohol y drogas?",
        "Cuando puede la empresa requerir pruebas de deteccion de consumo?",
        "En que consiste el programa o politica de pausas activas?",
        "Por que son importantes las pausas activas para la salud fisica?",
        "Como ayudan las pausas activas a la concentracion y al estres?",
        "Que recomendaciones de seguridad vial aparecen en el corpus?",
        "Que compromisos tiene el PESV o plan estrategico de seguridad vial?",
        "Que significa cero tolerancia frente a alcohol y sustancias en seguridad vial?",
        "Que documentos o reglas hablan de prevencion del acoso laboral?",
        "Cual es el objetivo general del manual de convivencia laboral?",
        "Cuales son los objetivos especificos del manual de convivencia?",
        "En cuanto tiempo debe el Comite de Convivencia dar tramite a una queja?",
        "Que ocurre si un integrante del Comite de Convivencia es parte de una queja o investigacion?",
        "Por que la seguridad vial es una responsabilidad compartida?",
        "Que programa incluye el PESV para proteger actores viales vulnerables?",
        "Que metodologia debe adoptar la empresa para mejorar continuamente la prevencion del riesgo vial?",
        "Que medidas preventivas y correctivas contempla el reglamento interno frente al acoso laboral y sexual?"
    ];

    public ManualChatbotDispatchLoadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Dispatches_61_questions_to_the_external_chatbot_api_with_10_to_15_second_spacing()
    {
        var options = ReadOptionsFromEnvironment();
        using var transport = new HttpClientHandler();
        using var loggingHandler = new LoggingHandler(_output) { InnerHandler = transport };
        using var http = new HttpClient(loggingHandler)
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds)
        };

        var client = new HttpChatbotDispatchClient(http, Options.Create(options));
        var conversationId = $"conv_manual_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var receipts = new List<ChatDispatchReceipt>(SstHybridQuestions.Length);

        _output.WriteLine($"[{DateTimeOffset.Now:O}] Starting manual dispatch run for {SstHybridQuestions.Length} questions.");
        _output.WriteLine($"base_url={options.BaseUrl}");
        _output.WriteLine($"project_id={options.ProjectId}");
        _output.WriteLine($"rag_variant_id={options.RagVariantId}");
        _output.WriteLine($"submit_path={options.SubmitPath}");
        _output.WriteLine($"releases_path_template={options.ReleasesPathTemplate}");
        _output.WriteLine($"conversation_id={conversationId}");
        _output.WriteLine($"top_k={TopK}");
        _output.WriteLine($"bearer_token_present={!string.IsNullOrWhiteSpace(options.BearerToken)}");

        for (var index = 0; index < SstHybridQuestions.Length; index++)
        {
            var questionNumber = index + 1;
            var question = SstHybridQuestions[index];
            var messageId = $"msg_manual_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{questionNumber:00}";

            _output.WriteLine($"[{DateTimeOffset.Now:O}] [{questionNumber}/{SstHybridQuestions.Length}] Preparing dispatch.");
            _output.WriteLine($"message_id={messageId}");
            _output.WriteLine($"question={question}");

            try
            {
                var receipt = await client.DispatchAsync(
                    new ChatQuestionSubmission(question, conversationId, messageId, TopK),
                    CancellationToken.None);

                receipts.Add(receipt);

                _output.WriteLine(
                    $"[{DateTimeOffset.Now:O}] Dispatch accepted. dispatch_id={receipt.DispatchId} " +
                    $"release={receipt.RagReleaseId} chunks_sent={receipt.ChunksSent} " +
                    $"webhook_status_code={receipt.WebhookStatusCode}");

                Assert.False(string.IsNullOrWhiteSpace(receipt.DispatchId));
                Assert.Equal(options.ProjectId, receipt.ProjectId);
                Assert.Equal(options.RagVariantId, receipt.RagVariantId);
                Assert.Equal(messageId, receipt.MessageId);
                Assert.Equal(question, receipt.Question);
                Assert.InRange(receipt.WebhookStatusCode, 200, 299);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"[{DateTimeOffset.Now:O}] Dispatch failed for message_id={messageId}");
                _output.WriteLine(ex.ToString());
                throw;
            }

            if (questionNumber == SstHybridQuestions.Length)
            {
                continue;
            }

            var delaySeconds = Random.Shared.Next(10, 16);
            _output.WriteLine($"[{DateTimeOffset.Now:O}] Waiting {delaySeconds} seconds before the next question.");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), CancellationToken.None);
        }

        Assert.Equal(SstHybridQuestions.Length, receipts.Count);
    }

    [Fact]
    public async Task Dispatches_61_questions_through_local_api_and_prints_chunks_per_question()
    {
        var baseUrl = ReadOptional("CHATBOT_LOCAL_API_BASE_URL", "http://localhost:5254");
        // Must exceed ChatbotDispatch.RequestTimeoutSeconds (the .NET->Python call this
        // POST waits on synchronously, which itself waits on the webhook->LLM chain).
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(240) };
        var conversationId = $"conv_chunks_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        // Optional subset for fast tuning iteration (e.g. 15 of 61, ~15 min instead of ~60 min).
        // Unset -> unchanged behavior (all 61).
        var limitRaw = Environment.GetEnvironmentVariable("CHATBOT_LOAD_TEST_QUESTION_LIMIT");
        var questions = int.TryParse(limitRaw, out var limit) && limit > 0
            ? SstHybridQuestions[..Math.Min(limit, SstHybridQuestions.Length)]
            : SstHybridQuestions;

        var results = new List<(int Number, string Question, string? RequestId, int ChunkCount, string? Answer, string? Error, double ElapsedSeconds)>(questions.Length);

        Log($"[{DateTimeOffset.Now:O}] Starting chunk-dispatch run for {questions.Length} questions.");
        Log($"local_api={baseUrl}");
        Log($"conversation_id={conversationId}");
        Log($"polling_timeout={PollingTimeout.TotalSeconds}s");
        Log($"polling_interval={PollingInterval.TotalSeconds}s");

        await EnsureLocalApiIsReachableAsync(http, baseUrl);

        var consecutiveFailures = 0;

        for (var index = 0; index < questions.Length; index++)
        {
            var questionNumber = index + 1;
            var question = questions[index];
            var messageId = $"msg_chunks_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{questionNumber:00}";

            Log($"---");
            Log($"[{DateTimeOffset.Now:O}] [{questionNumber}/{questions.Length}] {question}");

            string? requestId = null;
            int chunkCount = 0;
            string? answer = null;
            string? error = null;
            // Wall-clock the whole per-question chain (POST -> retrieval -> webhook -> LLM ->
            // settle). Started before the POST, read when we settle/fail/timeout below.
            var sw = Stopwatch.StartNew();

            try
            {
                var startBody = new { Question = question, ConversationId = conversationId, MessageId = messageId, TopK = TopK };
                using var startResp = await http.PostAsJsonAsync("/api/chat/requests", startBody, Json);
                var startJson = await startResp.Content.ReadAsStringAsync();

                if (!startResp.IsSuccessStatusCode)
                {
                    Log($"  START FAILED: {(int)startResp.StatusCode} {startResp.ReasonPhrase}");
                    Log($"  {startJson}");
                    error = $"start_failed_{(int)startResp.StatusCode}";
                    results.Add((questionNumber, question, null, 0, null, error, sw.Elapsed.TotalSeconds));
                    continue;
                }

                using var startDoc = JsonDocument.Parse(startJson);
                requestId = startDoc.RootElement.GetProperty("requestId").GetString();
                Log($"  requestId={requestId} -- polling for chunks...");

                var deadline = DateTimeOffset.UtcNow + PollingTimeout;
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(PollingInterval);

                    using var pollResp = await http.GetAsync($"/api/chat/requests/{requestId}");
                    if (!pollResp.IsSuccessStatusCode)
                    {
                        Log($"  POLL {(int)pollResp.StatusCode} -- retrying...");
                        continue;
                    }

                    var pollJson = await pollResp.Content.ReadAsStringAsync();
                    using var pollDoc = JsonDocument.Parse(pollJson);
                    var root = pollDoc.RootElement;
                    var state = root.GetProperty("state").GetString();

                    if (state is not "completed" and not "failed")
                    {
                        continue;
                    }

                    if (root.TryGetProperty("chunks", out var chunksArr) && chunksArr.ValueKind == JsonValueKind.Array)
                    {
                        chunkCount = chunksArr.GetArrayLength();
                        Log($"  STATE={state} -- {chunkCount} chunks received:");

                        for (var ci = 0; ci < chunksArr.GetArrayLength(); ci++)
                        {
                            var c = chunksArr[ci];
                            var docId = c.TryGetProperty("documentId", out var dv) ? dv.GetString() : "?";
                            var score = c.TryGetProperty("score", out var sv) ? sv.GetDouble().ToString("F4") : "?";
                            var source = c.TryGetProperty("source", out var srcv) ? srcv.GetString() : "?";
                            var pageStart = c.TryGetProperty("pageStart", out var ps) && ps.ValueKind != JsonValueKind.Null ? ps.GetInt32().ToString() : "-";
                            var pageEnd = c.TryGetProperty("pageEnd", out var pe) && pe.ValueKind != JsonValueKind.Null ? pe.GetInt32().ToString() : "-";
                            var section = c.TryGetProperty("sectionTitle", out var stv) && stv.ValueKind != JsonValueKind.Null ? stv.GetString() : null;
                            var text = c.TryGetProperty("text", out var tv) ? tv.GetString() : "";
                            var preview = text?.Length > 120 ? string.Concat(text.AsSpan(0, 120), "...") : text;

                            Log($"    [{ci + 1}] doc={docId} score={score} source={source} p{pageStart}-{pageEnd}" +
                                              (section is not null ? $" section=\"{section}\"" : ""));
                            Log($"        {preview}");
                        }
                    }
                    else
                    {
                        Log($"  STATE={state} -- 0 chunks (webhook may not have arrived yet)");
                    }

                    if (root.TryGetProperty("answer", out var av) && av.ValueKind != JsonValueKind.Null)
                    {
                        answer = av.GetString();
                        // Full generated answer, not a 200-char preview: this run is how we
                        // judge answer quality question-by-question.
                        Log($"  ELAPSED: {sw.Elapsed.TotalSeconds:F1}s");
                        Log($"  ANSWER (full):");
                        Log($"    {answer?.Replace("\n", "\n    ")}");
                    }

                    if (state == "failed" && root.TryGetProperty("error", out var ev) && ev.ValueKind != JsonValueKind.Null)
                    {
                        error = ev.GetString();
                        Log($"  ERROR: {error}");
                    }

                    break;
                }

                if (chunkCount == 0 && error is null)
                {
                    Log($"  TIMEOUT after {PollingTimeout.TotalSeconds}s -- webhook did not complete");
                    error = "webhook_timeout";
                }
            }
            catch (Exception ex)
            {
                Log($"  EXCEPTION: {ex.Message}");
                error = ex.GetType().Name;
            }

            sw.Stop();
            results.Add((questionNumber, question, requestId, chunkCount, answer, error, sw.Elapsed.TotalSeconds));

            if (error is null)
            {
                consecutiveFailures = 0;
            }
            else
            {
                consecutiveFailures++;
                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    Log($"  ABORTING: {consecutiveFailures} consecutive failures (last: {error}). " +
                        "Same root cause is almost certainly failing every remaining question -- " +
                        "not burning the rest of the run to confirm that. Fix and rerun.");
                    break;
                }
            }

            if (questionNumber < questions.Length)
            {
                // Each question already blocks 40-90s+ through the synchronous
                // retrieval->webhook->LLM chain, and the loop is strictly sequential
                // (no concurrent LLM slot to cool down) -- this is just enough for
                // logs to flush before the next request, not real rate-limiting.
                var delaySeconds = Random.Shared.Next(2, 4);
                Log($"  Waiting {delaySeconds}s before next question...");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
        }

        Log($"===");
        Log($"SUMMARY -- {results.Count} questions dispatched");

        foreach (var r in results)
        {
            var status = r.Error ?? (r.Answer is not null ? "ok" : "no_answer");
            Log($"  #{r.Number,-3} {r.ElapsedSeconds,6:F1}s chunks={r.ChunkCount,-3} status={status,-16} {Truncate(r.Question, 60)}");
        }

        var withChunks = results.Count(r => r.ChunkCount > 0);
        var withAnswer = results.Count(r => r.Answer is not null);
        var failed = results.Count(r => r.Error is not null);

        Log($"");
        Log($"With chunks: {withChunks}/{results.Count}");
        Log($"With answer: {withAnswer}/{results.Count}");
        Log($"Failed:      {failed}/{results.Count}");

        // Per-question latency over the questions that actually answered (failures/timeouts
        // would skew the percentiles). P95 is the number that must land under the 25s target.
        var times = results.Where(r => r.Answer is not null).Select(r => r.ElapsedSeconds).OrderBy(t => t).ToArray();
        if (times.Length > 0)
        {
            double Pct(double p) => times[Math.Min(times.Length - 1, (int)Math.Round(p / 100.0 * (times.Length - 1)))];
            Log($"");
            Log($"Answer latency (n={times.Length}): " +
                $"P50={Pct(50):F1}s  P95={Pct(95):F1}s  max={times[^1]:F1}s  avg={times.Average():F1}s");
        }

        // A green test here used to mean nothing: it only asserted the loop ran,
        // never that any question actually succeeded. Assert real success so a
        // broken wire (0/61 chunks) fails loudly instead of reporting "Correcto".
        Assert.True(
            results.Count == questions.Length,
            $"Aborted early after {results.Count}/{questions.Length} questions " +
            $"({MaxConsecutiveFailures} consecutive failures). See log above for the root cause.");
        Assert.True(
            failed == 0,
            $"{failed}/{results.Count} questions failed. See log above for per-question errors.");
    }

    private void Log(string message)
    {
        Console.WriteLine(message);
        _output.WriteLine(message);
    }

    private async Task EnsureLocalApiIsReachableAsync(HttpClient http, string baseUrl)
    {
        try
        {
            using var response = await http.GetAsync("/health");
            var body = await response.Content.ReadAsStringAsync();

            Log($"health_status={(int)response.StatusCode} {response.ReasonPhrase}");
            if (!string.IsNullOrWhiteSpace(body))
            {
                Log(body);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Local API responded with {(int)response.StatusCode} {response.ReasonPhrase} at {baseUrl}/health.");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Local API is not reachable at {baseUrl}. Start app/back/src/Chatbot.Sst.Api before running this test. " +
                $"Original error: {ex.Message}",
                ex);
        }
    }

    private static string Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) ? "(empty)" : value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");

    private static ChatbotDispatchOptions ReadOptionsFromEnvironment()
    {
        var baseUrl = ReadRequired("CHATBOT_DISPATCH_BASE_URL");
        var bearerToken = ReadRequired("CHATBOT_DISPATCH_BEARER_TOKEN");

        return new ChatbotDispatchOptions
        {
            BaseUrl = baseUrl,
            BearerToken = bearerToken,
            ProjectId = ReadOptional("CHATBOT_DISPATCH_PROJECT_ID", "proj_sst-general"),
            RagVariantId = ReadOptional("CHATBOT_DISPATCH_RAG_VARIANT_ID", "ragv_local-bge"),
            SubmitPath = ReadOptional("CHATBOT_DISPATCH_SUBMIT_PATH", "/api/chatbot/questions"),
            ReleasesPathTemplate = ReadOptional(
                "CHATBOT_DISPATCH_RELEASES_PATH_TEMPLATE",
                "/api/platform/projects/{project_id}/releases?page=1&page_size=100"),
            DefaultTopK = TopK,
            RequestTimeoutSeconds = 180
        };
    }

    private static string ReadRequired(string name)
        => Environment.GetEnvironmentVariable(name)?.Trim() switch
        {
            { Length: > 0 } value => value,
            _ => throw new InvalidOperationException(
                $"Missing required environment variable: {name}. " +
                "Define it before running ManualChatbotDispatchLoadTests.")
        };

    private static string ReadOptional(string name, string fallback)
        => Environment.GetEnvironmentVariable(name)?.Trim() switch
        {
            { Length: > 0 } value => value,
            _ => fallback
        };

    private sealed class LoggingHandler : DelegatingHandler
    {
        private readonly ITestOutputHelper _output;

        public LoggingHandler(ITestOutputHelper output)
        {
            _output = output;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            _output.WriteLine($"[{DateTimeOffset.Now:O}] --> {request.Method} {request.RequestUri}");
            _output.WriteLine($"authorization_present={request.Headers.Authorization is not null}");

            if (!string.IsNullOrWhiteSpace(requestBody))
            {
                _output.WriteLine(requestBody);
            }

            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                stopwatch.Stop();

                var responseBody = response.Content is null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync(cancellationToken);

                _output.WriteLine(
                    $"[{DateTimeOffset.Now:O}] <-- {(int)response.StatusCode} {response.ReasonPhrase} " +
                    $"({stopwatch.ElapsedMilliseconds} ms)");

                if (!string.IsNullOrWhiteSpace(responseBody))
                {
                    _output.WriteLine(responseBody);
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _output.WriteLine(
                    $"[{DateTimeOffset.Now:O}] !! HTTP failure after {stopwatch.ElapsedMilliseconds} ms: {ex.GetType().Name}");
                _output.WriteLine(ex.Message);
                throw;
            }
        }
    }
}
