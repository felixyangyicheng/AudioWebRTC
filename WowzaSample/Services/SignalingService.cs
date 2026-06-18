using Microsoft.JSInterop;
using WowzaSample.Models;

namespace WowzaSample.Services;

/// <summary>
/// Manages signaling with WebRTCHub via a browser-side SignalR JS connection.
/// Each hub callback is forwarded from JS → .NET via DotNetObjectReference.
/// No server-side WebSocket — eliminates per-user server→localhost connections.
/// </summary>
public class SignalingService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<SignalingService>? _dotNetRef;
    private bool _started;

    public string? MyConnectionId { get; private set; }
    public string? MyUsername { get; private set; }

    // Hub → client callbacks
    public event Func<List<User>, Task>? OnUserListUpdated;
    public event Func<User, Task>? OnIncomingCall;
    public event Func<User, string, Task>? OnCallDeclined;
    public event Func<User, Task>? OnCallAccepted;
    public event Func<User, string, Task>? OnCallEnded;
    public event Func<User, string, Task>? OnSignalReceived;

    // Connection state callbacks
    public event Func<string, Task>? OnReconnecting;
    public event Func<string, Task>? OnReconnected;
    public event Func<string, Task>? OnDisconnected;

    public SignalingService(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>Start the browser-side SignalR connection and join the hub.</summary>
    public async Task StartAsync(string hubUrl, string username)
    {
        if (_started) return;

        MyUsername = username;
        _dotNetRef = DotNetObjectReference.Create(this);

        var connectionId = await _js.InvokeAsync<string>(
            "signalingBridge.start", hubUrl, _dotNetRef, username);

        MyConnectionId = connectionId;
        _started = true;
    }

    // ── Client → Server RPCs (forwarded through JS bridge) ──

    public async Task CallUserAsync(string targetConnectionId)
    {
        await _js.InvokeVoidAsync("signalingBridge.invoke", "CallUser",
            new User { ConnectionId = targetConnectionId });
    }

    public async Task AnswerCallAsync(bool accept, User caller)
    {
        await _js.InvokeVoidAsync("signalingBridge.invoke", "AnswerCall", accept, caller);
    }

    public async Task HangUpAsync()
    {
        await _js.InvokeVoidAsync("signalingBridge.invoke", "HangUp");
    }

    /// <summary>Re-join the hub after a reconnection to restore user state.</summary>
    public async Task RejoinAsync()
    {
        if (MyUsername != null)
            await _js.InvokeVoidAsync("signalingBridge.invoke", "Join", MyUsername);
    }

    public async Task SendSignalAsync(string signal, string targetConnectionId)
    {
        await _js.InvokeVoidAsync("signalingBridge.invoke", "SendSignal", signal, targetConnectionId);
    }

    // ── JS → .NET Callbacks (invoked by signalingBridge.js) ──

    [JSInvokable]
    public async Task OnUserListUpdatedFromJS(List<User> users)
    {
        if (OnUserListUpdated != null)
            await OnUserListUpdated.Invoke(users);
    }

    [JSInvokable]
    public async Task OnIncomingCallFromJS(User caller)
    {
        if (OnIncomingCall != null)
            await OnIncomingCall.Invoke(caller);
    }

    [JSInvokable]
    public async Task OnCallAcceptedFromJS(User user)
    {
        if (OnCallAccepted != null)
            await OnCallAccepted.Invoke(user);
    }

    [JSInvokable]
    public async Task OnCallDeclinedFromJS(User user, string reason)
    {
        if (OnCallDeclined != null)
            await OnCallDeclined.Invoke(user, reason);
    }

    [JSInvokable]
    public async Task OnCallEndedFromJS(User user, string reason)
    {
        if (OnCallEnded != null)
            await OnCallEnded.Invoke(user, reason);
    }

    [JSInvokable]
    public async Task OnSignalReceivedFromJS(User user, string signal)
    {
        if (OnSignalReceived != null)
            await OnSignalReceived.Invoke(user, signal);
    }

    [JSInvokable]
    public async Task OnReconnectingFromJS(string error)
    {
        if (OnReconnecting != null)
            await OnReconnecting.Invoke(error);
    }

    [JSInvokable]
    public async Task OnReconnectedFromJS(string connectionId)
    {
        MyConnectionId = connectionId;
        if (OnReconnected != null)
            await OnReconnected.Invoke(connectionId);
    }

    [JSInvokable]
    public async Task OnDisconnectedFromJS(string error)
    {
        _started = false;
        if (OnDisconnected != null)
            await OnDisconnected.Invoke(error);
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await _js.InvokeVoidAsync("signalingBridge.stop");
            _started = false;
        }
        _dotNetRef?.Dispose();
    }
}
