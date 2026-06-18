/**
 * SignalR Signaling Bridge for Blazor
 * Maintains a browser-side SignalR connection to the WebRTCHub
 * and bridges calls to/from .NET via DotNetObjectReference.
 * 
 * This replaces the server-side Microsoft.AspNetCore.SignalR.Client
 * (which created server→localhost WebSocket per user).
 */
window.signalingBridge = {
    _connection: null,
    _dotNetRef: null,

    /** Store the .NET reference and connect to the hub */
    start: function (hubUrl, dotNetRef, username) {
        var self = this;
        this._dotNetRef = dotNetRef;

        this._connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        // ── Hub → .NET callbacks ──
        this._connection.on('updateUserList', function (users) {
            self._dotNetRef.invokeMethodAsync('OnUserListUpdated', users);
        });
        this._connection.on('incomingCall', function (caller) {
            self._dotNetRef.invokeMethodAsync('OnIncomingCall', caller);
        });
        this._connection.on('callAccepted', function (user) {
            self._dotNetRef.invokeMethodAsync('OnCallAccepted', user);
        });
        this._connection.on('callDeclined', function (user, reason) {
            self._dotNetRef.invokeMethodAsync('OnCallDeclined', user, reason);
        });
        this._connection.on('callEnded', function (user, reason) {
            self._dotNetRef.invokeMethodAsync('OnCallEnded', user, reason);
        });
        this._connection.on('receiveSignal', function (user, signal) {
            self._dotNetRef.invokeMethodAsync('OnSignalReceived', user, signal);
        });

        // ── Connection state → .NET ──
        this._connection.onreconnecting(function (error) {
            self._dotNetRef.invokeMethodAsync('OnReconnecting', error ? error.message : '');
        });
        this._connection.onreconnected(function (connectionId) {
            self._dotNetRef.invokeMethodAsync('OnReconnected', connectionId || '');
        });
        this._connection.onclose(function (error) {
            self._dotNetRef.invokeMethodAsync('OnDisconnected', error ? error.message : '');
        });

        // Start and join
        return this._connection.start()
            .then(function () {
                console.log('Signaling: Connected, joining as ' + username);
                return self._connection.invoke('Join', username);
            })
            .then(function () {
                return self._connection.connectionId;
            });
    },

    /** Invoke a hub method */
    invoke: function (methodName) {
        var args = Array.prototype.slice.call(arguments, 1);
        return this._connection.invoke(methodName, ...args);
    },

    /** Stop and disconnect */
    stop: function () {
        if (this._connection) {
            return this._connection.stop();
        }
    }
};
