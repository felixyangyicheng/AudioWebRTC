using Microsoft.JSInterop;

namespace WowzaSample.Services;

/// <summary>
/// C# wrapper around the browser WebRTC APIs, invoked via IJSRuntime.
/// All real WebRTC work happens in webrtcInterop.js (the JS bridge).
/// </summary>
public class WebRTCService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<WebRTCService>? _dotNetRef;
    private bool _initialized;

    /// <summary>Fires when a new ICE candidate is available for a partner.</summary>
    public event Func<string, string?, Task>? OnIceCandidate;

    public WebRTCService(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>Initialize the JS bridge and register .NET callbacks.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("webrtcBridge.setDotNetRef", _dotNetRef);
        _initialized = true;
    }

    /// <summary>Request user audio media. Returns the audio device label.</summary>
    public async Task<string> GetUserMediaAsync()
    {
        await EnsureInitialized();
        return await _js.InvokeAsync<string>("webrtcBridge.getUserMedia");
    }

    /// <summary>Stop and release the local media stream.</summary>
    public async Task StopLocalStreamAsync()
    {
        await EnsureInitialized();
        await _js.InvokeVoidAsync("webrtcBridge.stopLocalStream");
    }

    /// <summary>Toggle microphone mute. Returns true if now muted.</summary>
    public async Task<bool> ToggleMuteAsync()
    {
        await EnsureInitialized();
        return await _js.InvokeAsync<bool>("webrtcBridge.toggleMute");
    }

    /// <summary>Check if microphone is muted.</summary>
    public async Task<bool> IsMutedAsync()
    {
        await EnsureInitialized();
        return await _js.InvokeAsync<bool>("webrtcBridge.isMuted");
    }

    /// <summary>Play incoming call ringtone.</summary>
    public async Task PlayRingtoneAsync()
    {
        await EnsureInitialized();
        await _js.InvokeVoidAsync("webrtcBridge.playRingtone");
    }

    /// <summary>Stop the ringtone.</summary>
    public async Task StopRingtoneAsync()
    {
        await EnsureInitialized();
        await _js.InvokeVoidAsync("webrtcBridge.stopRingtone");
    }

    /// <summary>Create an RTCPeerConnection for a partner.</summary>
    public async Task CreatePeerConnectionAsync(string partnerId)
    {
        await EnsureInitialized();
        await _js.InvokeVoidAsync("webrtcBridge.createPeerConnection", partnerId);
    }

    /// <summary>Create an SDP offer for the partner.</summary>
    public async Task<string> CreateOfferAsync(string partnerId)
    {
        await EnsureInitialized();
        return await _js.InvokeAsync<string>("webrtcBridge.createOffer", partnerId);
    }

    /// <summary>Create an SDP answer for the partner.</summary>
    public async Task<string> CreateAnswerAsync(string partnerId)
    {
        await EnsureInitialized();
        return await _js.InvokeAsync<string>("webrtcBridge.createAnswer", partnerId);
    }

    /// <summary>Apply the remote SDP description.</summary>
    public async Task SetRemoteDescriptionAsync(string partnerId, string sdpJson)
    {
        await EnsureInitialized();
        await _js.InvokeVoidAsync("webrtcBridge.setRemoteDescription", partnerId, sdpJson);
    }

    /// <summary>Add a remote ICE candidate (null = end of candidates).</summary>
    public async Task AddIceCandidateAsync(string partnerId, string? candidateJson)
    {
        await EnsureInitialized();
        await _js.InvokeVoidAsync("webrtcBridge.addIceCandidate", partnerId, candidateJson);
    }

    /// <summary>Close the peer connection for a partner.</summary>
    public async Task CloseConnectionAsync(string partnerId)
    {
        await EnsureInitialized();
        await _js.InvokeVoidAsync("webrtcBridge.closeConnection", partnerId);
    }

    /// <summary>Close all peer connections.</summary>
    public async Task CloseAllConnectionsAsync()
    {
        await EnsureInitialized();
        await _js.InvokeVoidAsync("webrtcBridge.closeAllConnections");
    }

    // ── Callbacks from JavaScript ──

    /// <summary>Called by JS when an ICE candidate is gathered or gathering completes (null).</summary>
    [JSInvokable]
    public async Task OnIceCandidateFromJS(string partnerId, string? candidateJson)
    {
        if (OnIceCandidate != null)
            await OnIceCandidate.Invoke(partnerId, candidateJson);
    }

    // ── Helpers ──

    private async Task EnsureInitialized()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            await CloseAllConnectionsAsync();
            await StopLocalStreamAsync();
        }
        _dotNetRef?.Dispose();
    }
}
