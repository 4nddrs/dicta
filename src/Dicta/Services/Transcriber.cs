using System.Text;
using Whisper.net;

namespace Dicta.Services;

sealed class Transcriber : IDisposable
{
    private const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _modelPath;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private Task? _prepare;

    public Transcriber()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "models", "ggml-base.en.bin");
        var local = Path.Combine(AppPaths.StateDirectory, "models", "ggml-base.en.bin");
        _modelPath = Ready(bundled) ? bundled : local;
    }

    public bool ModelExists => Ready(_modelPath);

    private static bool Ready(string path) => File.Exists(path) && new FileInfo(path).Length > 100_000_000;

    public event Action<double>? Progress;

    public Task PrepareAsync()
    {
        return _prepare ??= PrepareCoreAsync();
    }

    public async Task<string> TranscribeAsync(byte[] pcm16k, CancellationToken cancellationToken)
    {
        await PrepareAsync().ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_processor == null)
                throw new InvalidOperationException("The voice model is not ready.");

            using var wav = AudioUtil.ToWav(pcm16k, 16_000);
            var text = new StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(wav, cancellationToken).ConfigureAwait(false))
                text.Append(segment.Text);

            return Clean(text.ToString());
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        _gate.Dispose();
    }

    private async Task PrepareCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_processor != null)
                return;

            if (!ModelExists)
                await DownloadAsync().ConfigureAwait(false);

            Progress?.Invoke(-1);
            _factory = WhisperFactory.FromPath(_modelPath);
            var threads = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
            _processor = _factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(threads)
                .WithGreedySamplingStrategy()
                .WithNoContext()
                .WithPrompt("English dictation with punctuation.")
                .Build();
            Progress?.Invoke(100);
            Log.Info("Model ready.");
        }
        catch
        {
            _prepare = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DownloadAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
        var partial = _modelPath + ".part";
        if (File.Exists(partial))
            File.Delete(partial);

        Progress?.Invoke(0);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Dicta/1.0");

        try
        {
            using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0L;
            await using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var output = File.Create(partial))
            {
                var buffer = new byte[1024 * 128];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    readTotal += read;
                    if (total > 0)
                        Progress?.Invoke(Math.Clamp(readTotal * 100.0 / total, 0, 99));
                }
            }

            File.Move(partial, _modelPath, overwrite: true);
            if (!ModelExists)
                throw new InvalidOperationException("The model download was incomplete.");
        }
        catch
        {
            try
            {
                if (File.Exists(partial))
                    File.Delete(partial);
            }
            catch
            {
                // The next dictation retries the download.
            }

            throw;
        }
    }

    private static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);

        var comparable = text.Trim().Trim('.', '!', '?', ' ').ToLowerInvariant();
        if (comparable is "thanks for watching"
            or "thank you for watching"
            or "thanks for watching!"
            or "subtitles by the amara.org community"
            or "[music]"
            or "(music)"
            or "music")
            return "";

        return text;
    }
}
