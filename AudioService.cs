using Microsoft.JSInterop;
using System;
using System.Net.Http;
using System.Net.Http.Headers; 
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using System.IO;
using System.Text.Json;

namespace NetworkMonitorChat
{
    public class AudioService : IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private IJSObjectReference? _jsModule;
        private bool _isInitialized = false;
        private readonly Queue<string> _audioQueue = new Queue<string>();
        private bool _isPlaying = false;
        private CancellationTokenSource? _playbackCts;
        private readonly object _lock = new object();
        private string _apiUrl;
          private readonly HttpClient _httpClient;


        public AudioService(IJSRuntime jsRuntime, NetConnectConfig netConfig)
        {
            _jsRuntime = jsRuntime;
            _apiUrl= netConfig.TranscribeAudioUrl;
            Console.WriteLine($"Using transcribe url: {_apiUrl}");
             _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(30) // Set appropriate timeout
        };
        }

        private async Task EnsureInitialized()
        {
            if (!_isInitialized)
            {
                _jsModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./js/chatInterop.js");
                _isInitialized = true;
            }
        }

        public async Task PlayAudioSequentially(string audioFile)
        {
            await EnsureInitialized();

            lock (_lock)
            {
                _audioQueue.Enqueue(audioFile);
                if (!_isPlaying)
                {
                    _ = ProcessQueueAsync(); // Fire and forget
                }
            }
        }

        private async Task ProcessQueueAsync()
        {
            lock (_lock)
            {
                if (_isPlaying) return;
                _isPlaying = true;
                _playbackCts = new CancellationTokenSource();
            }

            try
            {
                while (true)
                {
                    string nextAudio;
                    lock (_lock)
                    {
                        if (_audioQueue.Count == 0 || _playbackCts?.IsCancellationRequested == true)
                        {
                            break;
                        }
                        nextAudio = _audioQueue.Dequeue();
                    }

                    try
                    {
                        // Create a promise that completes when audio finishes
                        var tcs = new TaskCompletionSource<bool>();
                        var dotnetRef = DotNetObjectReference.Create(new AudioCallbackHelper(tcs));

                        await _jsRuntime.InvokeVoidAsync(
                            "chatInterop.playAudioWithCallback",
                            nextAudio,
                            dotnetRef);

                        await tcs.Task.WaitAsync(_playbackCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        // Playback was cancelled
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error playing audio: {ex}");
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    _isPlaying = false;
                    _playbackCts?.Dispose();
                    _playbackCts = null;
                }
            }
        }

        public async Task PauseAudio()
        {
            await EnsureInitialized();
            lock (_lock)
            {
                _playbackCts?.Cancel();
            }
            await _jsRuntime.InvokeVoidAsync("chatInterop.pauseAudio");
        }

        public async Task ClearQueue()
        {
            await EnsureInitialized();
            lock (_lock)
            {
                _audioQueue.Clear();
                _playbackCts?.Cancel();
            }
            await _jsRuntime.InvokeVoidAsync("chatInterop.pauseAudio");
        }

        public async Task<bool> StartRecording(string recordingSessionId)
        {
            await EnsureInitialized();
            return await _jsRuntime.InvokeAsync<bool>(
                "chatInterop.startRecording", new object[] { recordingSessionId });
        }

   public async Task<byte[]> StopRecording(string recordingSessionId)
{
    try
    {
        // Invoke the JS function and get a stream reference instead of a large string
        var jsStreamRef = await _jsRuntime.InvokeAsync<IJSStreamReference>(
            "chatInterop.stopRecording", recordingSessionId);

        if (jsStreamRef == null)
        {
            // No stream returned (maybe recording never started or was already stopped)
            return Array.Empty<byte>();
        }

        // Read the stream into a MemoryStream (set maxAllowedSize as needed, e.g. 50MB)
        await using var dataStream = await jsStreamRef.OpenReadStreamAsync(maxAllowedSize: 50_000_000);
        using var ms = new MemoryStream();
        await dataStream.CopyToAsync(ms);
       var result=ms.ToArray();
        Console.Error.WriteLine($"Got array of data length {result.Length}");
       
        return result;
    }
    catch (JSException jsEx)
    {
        // Handle JavaScript errors (e.g. function not found or JS execution error)
        Console.Error.WriteLine($"JSInterop error in StopRecording: {jsEx.Message}");
        return Array.Empty<byte>();
    }
    catch (OperationCanceledException cancelEx)
    {
        // Handle timeout or cancellation if any token was used
        Console.Error.WriteLine($"StopRecording was canceled: {cancelEx.Message}");
        return Array.Empty<byte>();
    }
    catch (Exception ex)
    {
        // Catch all other errors
        Console.Error.WriteLine($"Unexpected error stopping recording: {ex}");
        return Array.Empty<byte>();
    }
}


public async Task<TResultObj<string>> TranscribeAudio(byte[] webmAudio)
{
    var result = new TResultObj<string>();
    using var content = new MultipartFormDataContent();
    using var audioContent = new ByteArrayContent(webmAudio);

    audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/webm");
    content.Add(audioContent, "file", "recording.webm");

    try
    {
        var response = await _httpClient.PostAsync(_apiUrl, content);

        var responseString = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Transcribe API Response: {responseString}");

        using var doc = JsonDocument.Parse(responseString);
        var root = doc.RootElement;

        var status = root.GetProperty("status").GetString();

        if (status?.Equals("success", StringComparison.OrdinalIgnoreCase) == true)
        {
            var transcription = root.GetProperty("transcription").GetString();
            result.Success = true;
            result.Message = "Transcription successful.";
            result.Data = transcription;
        }
        else if (status?.Equals("error", StringComparison.OrdinalIgnoreCase) == true)
        {
            var errorMessage = root.GetProperty("message").GetString();
            result.Success = false;
            result.Message = $"Transcription failed: {errorMessage}";
            result.Data = null;
        }
        else
        {
            result.Success = false;
            result.Message = "Unexpected response format.";
            result.Data = null;
        }
    }
    catch (Exception ex)
    {
        result.Success = false;
        result.Message = $"Exception during transcription: {ex.Message}";
        result.Data = null;
        Console.Error.WriteLine($"TranscribeAudio error: {ex}");
    }

    return result;
}
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ClearQueue();
                if (_jsModule is not null)
                {
                    await _jsModule.DisposeAsync();
                }
                _playbackCts?.Dispose();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error disposing AudioService: {ex}");
            }
        }

        private class AudioCallbackHelper
        {
            private readonly TaskCompletionSource<bool> _tcs;

            public AudioCallbackHelper(TaskCompletionSource<bool> tcs)
            {
                _tcs = tcs;
            }

            [JSInvokable]
            public void OnAudioEnded()
            {
                _tcs.TrySetResult(true);
            }
        }
    }
}