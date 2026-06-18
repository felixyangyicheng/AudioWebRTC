# WebRTC Audio Caller — Blazor Edition

Peer-to-peer audio calling demo using **WebRTC** for media and **SignalR** for signaling, built with **ASP.NET Core Blazor**.

Originally an MVC + jQuery app (ASP.NET Core 2.1), now migrated to **.NET 11 Blazor Interactive Server** with a pure C# UI and minimal JS bridges.

## Architecture

```
Browser                         Server
┌──────────────────────┐       ┌──────────────────────────────┐
│  Blazor Circuit      │◄──────►  Home.razor (C# UI)          │
│  (/_blazor)          │       │  ┌──────────────────────────┐│
│                      │       │  │ SignalingService         ││
│  SignalR Hub Client  │◄──────►│ (browser-side JS bridge)   ││
│  (/Hubs/WebRTCHub)   │       │  ├──────────────────────────┤│
│                      │       │  │ WebRTCService            ││
│  WebRTC (P2P) ────────────────│ (IJSRuntime → JS bridge)   ││
│  webrtcInterop.js    │       │  ├──────────────────────────┤│
│                      │       │  │ WebRTCHub (SignalR Hub)  ││
│  signalingBridge.js  │       │  │ ToastService             ││
└──────────────────────┘       │  └──────────────────────────┘│
                               └──────────────────────────────┘
```

- **SignalR Hub** (`WebRTCHub.cs`) — unchanged signaling state machine (Join, CallUser, AnswerCall, HangUp, SendSignal)
- **Signaling** — lightweight JS bridge (`signalingBridge.js`) maintains the browser's SignalR connection; C# `SignalingService` invokes hub methods via JS interop
- **WebRTC** — `WebRTCService.cs` (C#) wraps all browser WebRTC APIs through `webrtcInterop.js` (JS bridge)
- **UI** — Blazor component `Home.razor` with native dialogs, toast notifications, connection status bar

## Project Structure

```
AudioWebRTC/
├── WowzaSample.Shared/              # Shared models + hub interface
│   ├── Models/WebRTCModels.cs       # User, UserCall, CallOffer
│   └── Hubs/IWebRTCHub.cs          # Strongly-typed hub interface
├── WowzaSample/                     # Blazor Web App (InteractiveServer)
│   ├── Program.cs                   # Blazor + SignalR + CORS config
│   ├── Components/
│   │   ├── App.razor                # HTML shell, JS includes, TURN config
│   │   ├── Routes.razor
│   │   ├── Layout/MainLayout.razor
│   │   ├── Pages/Home.razor         # Main call UI (C#)
│   │   └── Dialogs/
│   │       ├── UserNamePrompt.razor  # Username entry modal
│   │       ├── IncomingCallDialog.razor  # Accept/decline modal
│   │       ├── ToastContainer.razor  # Toast notification overlay
│   │       └── ToastService.cs
│   ├── Hubs/WebRTCHub.cs            # SignalR signaling hub
│   ├── Services/
│   │   ├── WebRTCService.cs         # C# WebRTC API wrapper
│   │   └── SignalingService.cs      # Browser-side SignalR bridge
│   └── wwwroot/Scripts/
│       ├── webrtcInterop.js         # JS bridge: getUserMedia, RTCPeerConnection
│       └── signalingBridge.js       # JS bridge: SignalR hub client
├── WowzaSample.Client/              # Blazor WASM bootstrap (future Auto mode)
└── WowzaSample.Tests/               # xUnit + Moq (22 tests)
```

## Quick Start

### Prerequisites
- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) (preview)
- A modern browser (Chrome, Edge, Firefox, Opera)

### Run
```bash
dotnet run --project WowzaSample
```
Open two browser tabs at `https://localhost:5001`. Enter a username in each, then click a user to call.

### Test
```bash
dotnet test
```

## Key Features

- **Audio-only** one-to-one calls via WebRTC
- **Blazor native UI** — no jQuery, no alertify, no hand-written DOM manipulation
- **Connection status bar** — live feedback on SignalR connectivity (connected / reconnecting / disconnected)
- **Ringtone** — Web Audio API tone on incoming calls
- **Call timer** — live `mm:ss` display during active calls
- **Mute toggle** — microphone mute/unmute button
- **Tab title notification** — flashes "📞 Incoming Call" when someone calls
- **Toast notifications** — non-blocking success/warning/error messages
- **Auto-reconnect** — SignalR client reconnects on connection loss with user feedback
- **TURN/STUN** — configurable ICE servers via `window.appConfig` in `App.razor`

## Configuration

### ICE Servers
TURN credentials are set in `Components/App.razor` via `window.appConfig`:
```js
window.appConfig = {
    turnServer: {
        urls: "turn:yourserver:3478?transport=udp",
        username: "your-user",
        credential: "your-pass"
    }
};
```

### CORS
Restricted to localhost origins in `Program.cs`. For production, add your domain(s) to `WithOrigins(...)`.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core 11.0 (preview) |
| UI | Blazor Interactive Server |
| Real-time | SignalR (signaling) + WebRTC (media) |
| Testing | xUnit 2.9 + Moq 4.20 |
| Browser APIs | getUserMedia, RTCPeerConnection, Web Audio API |
