using System.Net;
using System.Reflection;
using System.Text;
using Dicta.Services;
using Xunit;

namespace Dicta.Tests;

internal sealed class StubHandler : HttpMessageHandler
{
    public string? Uri;
    public string? Authorization;
    public string? ContentType;
    public byte[]? Body;
    public Func<HttpResponseMessage> Respond = () => new(HttpStatusCode.OK);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uri = request.RequestUri!.ToString();
        Authorization = request.Headers.Authorization?.ToString();
        ContentType = request.Content!.Headers.ContentType!.ToString();
        Body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        return Respond();
    }
}

public class CloudTranscriberTests
{
    private const string Key = "sk-TEST-0123456789";

    private static (CloudTranscriber, StubHandler) Make(Func<HttpResponseMessage> respond)
    {
        var transcriber = new CloudTranscriber(Key, null);
        var stub = new StubHandler { Respond = respond };
        typeof(CloudTranscriber)
            .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(transcriber, new HttpClient(stub));
        return (transcriber, stub);
    }

    [Fact]
    public async Task SendsTheRecordingToOpenAi()
    {
        var (transcriber, stub) = Make(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"text\":\"hello there\"}"),
        });

        var text = await transcriber.TranscribeAsync(new byte[3_200], CancellationToken.None);

        Assert.Equal("hello there", text);
        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", stub.Uri);
        Assert.Equal("Bearer " + Key, stub.Authorization);

        var body = Encoding.UTF8.GetString(stub.Body!);
        Assert.Contains("name=file; filename=dictation.wav", body);
        Assert.Contains("Content-Type: audio/wav", body);
        Assert.Contains("name=model\r\n\r\ngpt-transcribe", body);
        Assert.Contains("name=response_format\r\n\r\njson", body);
        Assert.Contains("RIFF", body);
    }

    [Fact]
    public async Task RejectedKeyIsPermanentAndLeaksNothing()
    {
        var (transcriber, _) = Make(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Incorrect API key provided: " + Key + "\"}}"),
        });

        var error = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => transcriber.TranscribeAsync(new byte[1_024], CancellationToken.None));

        Assert.False(error.Transient);
        Assert.DoesNotContain("sk-", error.Message);
        Assert.DoesNotContain("sk-", error.ToString());
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(429, false)]
    [InlineData(400, false)]
    [InlineData(402, false)]
    public async Task FailuresReportWhetherARetryHelps(int status, bool transient)
    {
        var (transcriber, _) = Make(() => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("error"),
        });

        var error = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => transcriber.TranscribeAsync(new byte[1_024], CancellationToken.None));

        Assert.Equal(transient, error.Transient);
    }

    [Fact]
    public async Task ProjectWithoutTheModelIsNotARejectedKey()
    {
        // A project without the model answers 403 with model_not_found. The key itself is valid.
        var (transcriber, _) = Make(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":{\"code\":\"model_not_found\",\"message\":\"Project does not have access to model gpt-transcribe\"}}"),
        });

        var error = await Assert.ThrowsAsync<ModelUnavailableException>(
            () => transcriber.TranscribeAsync(new byte[1_024], CancellationToken.None));

        Assert.Equal("gpt-transcribe", error.Model);
        Assert.DoesNotContain("rejected", error.Message);
        Assert.DoesNotContain("key", error.Message);
    }

    [Fact]
    public async Task ForbiddenWithoutAModelCodeIsAPermissionProblem()
    {
        var (transcriber, _) = Make(() => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":{\"code\":\"insufficient_permissions\",\"message\":\"Missing scopes\"}}"),
        });

        var error = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => transcriber.TranscribeAsync(new byte[1_024], CancellationToken.None));

        Assert.False(error.Transient);
        Assert.DoesNotContain("rejected", error.Message);
        Assert.Contains("role", error.Message);
    }

    [Fact]
    public async Task NetworkFailureIsTransient()
    {
        var (transcriber, _) = Make(() => throw new HttpRequestException("refused"));

        var error = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => transcriber.TranscribeAsync(new byte[1_024], CancellationToken.None));

        Assert.True(error.Transient);
        Assert.Equal("Couldn't reach OpenAI. Check the connection and try again.", error.Message);
    }

    [Fact]
    public void PreferredModelsStartWithTheBestAndSkipDiarization()
    {
        Assert.Equal("gpt-transcribe", CloudTranscriber.PreferredModels[0]);
        Assert.DoesNotContain(CloudTranscriber.PreferredModels, model => model.Contains("diarize", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(CloudTranscriber.PreferredModels.Count, CloudTranscriber.PreferredModels.Distinct().Count());
    }

    [Fact]
    public void ModelDefaultsAndTrims()
    {
        Assert.Equal("gpt-transcribe", new CloudTranscriber(Key, null).Model);
        Assert.Equal("whisper-1", new CloudTranscriber(Key, " whisper-1 ").Model);
    }
}
