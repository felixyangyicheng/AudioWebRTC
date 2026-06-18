using Microsoft.AspNetCore.SignalR.Client;
using WowzaSample.Models;

namespace WowzaSample.Services;

/// <summary>
/// Manages the SignalR connection to the WebRTCHub for WebRTC call signaling.
/// Replaces the JavaScript SignalR client from connectionHub.js.
/// </summary>
public class SignalingService : IAsyncDisposable
{
    private HubConnection? _hub;
    private readonly string _hubUrl;

    public string? MyConnectionId { get; private set; }
    public string? MyUsername { get; private set; }

    // Server → Client callbacks (mirrors IWebRTCHub)
    public event Func<List<User>, Task>? OnUserListUpdated;
    public event Func<User, Task>? OnIncomingCall;
    public event Func<User, string, Task>? OnCallDeclined;
    public event Func<User, Task>? OnCallAccepted;
    public event Func<User, string, Task>? OnCallEnded;
    public event Func<User, string, Task>? OnSignalReceived;

    public SignalingService(string hubUrl)
    {
        _hubUrl = hubUrl;
    }

    /// <summary>Build and start the SignalR connection.</summary>
    public async Task StartAsync(string username)
    {
        MyUsername = username;

        _hub = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // ── Register server→client callbacks ──
        _hub.On<List<User>>("updateUserList", async (users) =>
        {
            if (OnUserListUpdated != null)
                await OnUserListUpdated.Invoke(users);
        });

        _hub.On<User>("incomingCall", async (caller) =>
        {
            if (OnIncomingCall != null)
                await OnIncomingCall.Invoke(caller);
        });

        _hub.On<User, string>("callDeclined", async (user, reason) =>
        {
            if (OnCallDeclined != null)
                await OnCallDeclined.Invoke(user, reason);
        });

        _hub.On<User>("callAccepted", async (user) =>
        {
            if (OnCallAccepted != null)
                await OnCallAccepted.Invoke(user);
        });

        _hub.On<User, string>("callEnded", async (user, reason) =>
        {
            if (OnCallEnded != null)
                await OnCallEnded.Invoke(user, reason);
        });

        _hub.On<User, string>("receiveSignal", async (user, signal) =>
        {
            if (OnSignalReceived != null)
                await OnSignalReceived.Invoke(user, signal);
        });

        // Start connection and join
        await _hub.StartAsync();
        MyConnectionId = _hub.ConnectionId;
        await _hub.InvokeAsync("Join", username);
    }

    // ── Client → Server RPCs ──

    public async Task CallUserAsync(string targetConnectionId)
    {
        if (_hub == null) return;
        await _hub.InvokeAsync("CallUser", new User { ConnectionId = targetConnectionId });
    }

    public async Task AnswerCallAsync(bool accept, User caller)
    {
        if (_hub == null) return;
        await _hub.InvokeAsync("AnswerCall", accept, caller);
    }

    public async Task HangUpAsync()
    {
        if (_hub == null) return;
        await _hub.InvokeAsync("HangUp");
    }

    public async Task SendSignalAsync(string signal, string targetConnectionId)
    {
        if (_hub == null) return;
        await _hub.InvokeAsync("SendSignal", signal, targetConnectionId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
        }
    }
}
