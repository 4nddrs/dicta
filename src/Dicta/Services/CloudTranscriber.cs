using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dicta.Services;

sealed class CloudTranscriptionException : Exception
{
    public CloudTranscriptionException(string message, bool transient) : base(message) => Transient = transient;

    public bool Transient { get; }
}

sealed class ModelUnavailableException : Exception
{
    public ModelUnavailableException(string model)
        : base($"The OpenAI project doesn't have access to {model}.") => Model = model;

    public string Model { get; }
}

sealed class CloudTranscriber : IDisposable
{
    public const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";
    public const string DefaultModel = "gpt-transcribe";

    /// <summary>
    /// Best English dictation models first. Diarization models are left out on purpose:
    /// they return speaker segments instead of the text that belongs in the cursor field.
    /// </summary>
    public static IReadOnlyList<string> PreferredModels { get; } = new[]
    {
        DefaultModel,
        "gpt-4o-transcribe",
        "gpt-4o-mini-transcribe",
        "whisper-1",
    };

    private static readonly Regex KeyLike = new(@"sk-[A-Za-z0-9_\-*]{6,}", RegexOptions.Compiled);

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _apiKey;
    private readonly string _model;

    public CloudTranscriber(string apiKey, string? model)
    {
        _apiKey = apiKey.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
    }

    public string Model => _model;

    public async Task<string> TranscribeAsync(byte[] pcm16k, CancellationToken cancellationToken)
    {
        using var wav = AudioUtil.ToWav(pcm16k, 16_000);
        using var form = new MultipartFormDataContent();
        using var audio = new StreamContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "dictation.wav");
        form.Add(new StringContent(_model), "model");
        form.Add(new StringContent("json"), "response_format");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CloudTranscriptionException("OpenAI took too long to answer. Try again.", transient: true);
        }
        catch (HttpRequestException)
        {
            throw new CloudTranscriptionException("Couldn't reach OpenAI. Check the connection and try again.", transient: true);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw Failure(response.StatusCode, body);

            return Parse(body);
        }
    }

    public void Dispose() => _client.Dispose();

    private static string Parse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString() ?? "";
        }
        catch (JsonException)
        {
            // Falls through to the message below.
        }

        throw new CloudTranscriptionException("OpenAI sent a response Dicta couldn't read. Try again.", transient: true);
    }

    private CloudTranscriptionException Failure(HttpStatusCode status, string body)
    {
        var code = ErrorCode(body);

        if (status is HttpStatusCode.Unauthorized)
        {
            // The provider echoes part of the key in this response, so it never reaches the log.
            Log.Error($"OpenAI rejected the key (HTTP {(int)status}).");
        }
        else if (code == "model_not_found")
        {
            // A project without access to the model answers 403, not 400. The key itself is fine.
            Log.Error($"OpenAI project has no access to {_model} (HTTP {(int)status}).");
        }
        else
        {
            Log.Error($"OpenAI transcription failed with HTTP {(int)status}. {Shorten(body)}");
        }

        if (code == "model_not_found")
            throw new ModelUnavailableException(_model);

        return status switch
        {
            HttpStatusCode.Unauthorized =>
                new CloudTranscriptionException("OpenAI rejected the key. Open the tray menu, choose OpenAI key, and paste it again.", transient: false),
            HttpStatusCode.Forbidden =>
                new CloudTranscriptionException($"The key isn't allowed to transcribe with {_model}. Check its role and the project's model access.", transient: false),
            HttpStatusCode.TooManyRequests =>
                new CloudTranscriptionException("OpenAI is busy or the account is out of credit. Try again in a minute.", transient: false),
            HttpStatusCode.PaymentRequired =>
                new CloudTranscriptionException("The OpenAI account needs a payment method. Check the billing page.", transient: false),
            HttpStatusCode.BadRequest =>
                new CloudTranscriptionException("OpenAI couldn't read that audio. Try again.", transient: false),
            _ when (int)status >= 500 =>
                new CloudTranscriptionException("OpenAI had a problem. Try again.", transient: true),
            _ => new CloudTranscriptionException($"OpenAI returned an error ({(int)status}). Try again.", transient: false),
        };
    }

    private static string? ErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error))
                return null;
            if (!error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String)
                return null;
            return code.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Shorten(string body)
    {
        var text = KeyLike.Replace(body, "sk-REDACTED");
        text = text.Trim().Replace('\n', ' ').Replace('\r', ' ');
        return text.Length <= 180 ? text : text[..180];
    }
}
