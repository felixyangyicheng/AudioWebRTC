/**
 * WebRTC JS Interop Bridge for Blazor
 * Exposes browser WebRTC APIs to C# via IJSRuntime.
 * Designed to be called from WebRTCService.cs in the Blazor client.
 */
window.webrtcBridge = {
    // ── State ──
    _connections: {},
    _localStream: null,
    _dotNetRef: null,

    // Peer connection config — TURN from server-rendered appConfig, STUN hardcoded
    _peerConfig: {
        iceServers: [
            { urls: "stun:stun.l.google.com:19302" },
            { urls: "stun:numb.viagenie.ca:3478" },
            (window.appConfig && window.appConfig.turnServer) ? window.appConfig.turnServer : null,
            { urls: "turn:turn-testdrive.cloudapp.net:3478", username: "redmond", credential: "redmond123" }
        ].filter(Boolean)
    },

    // ── Initialization ──

    /** Store the .NET object reference for callbacks */
    setDotNetRef: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
    },

    // ── Media ──

    /** Get user audio media */
    getUserMedia: function () {
        var self = this;
        return navigator.mediaDevices.getUserMedia({ audio: true, video: false })
            .then(function (stream) {
                self._localStream = stream;
                var tracks = stream.getAudioTracks();
                var label = tracks.length > 0 ? tracks[0].label : "Unknown";
                console.log("WebRTC: Got audio stream: " + label);
                return label;
            })
            .catch(function (err) {
                console.error("WebRTC: getUserMedia error", err);
                throw err.message || JSON.stringify(err);
            });
    },

    /** Stop all local tracks */
    stopLocalStream: function () {
        if (this._localStream) {
            this._localStream.getTracks().forEach(function (t) { t.stop(); });
            this._localStream = null;
        }
    },

    /** Toggle mute on the local audio track */
    toggleMute: function () {
        if (this._localStream) {
            var audioTracks = this._localStream.getAudioTracks();
            audioTracks.forEach(function (t) { t.enabled = !t.enabled; });
            return !audioTracks[0].enabled; // return true if now muted
        }
        return false;
    },

    /** Check if currently muted */
    isMuted: function () {
        if (this._localStream) {
            var audioTracks = this._localStream.getAudioTracks();
            return audioTracks.length > 0 && !audioTracks[0].enabled;
        }
        return false;
    },

    /** Play a simple ringtone using Web Audio API */
    playRingtone: function () {
        try {
            var ctx = new (window.AudioContext || window.webkitAudioContext)();
            var osc = ctx.createOscillator();
            var gain = ctx.createGain();
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.type = 'sine';
            gain.gain.value = 0.15;
            // Alternate between 440Hz and 550Hz every 500ms
            var playing = true;
            this._ringtoneStop = function () { playing = false; osc.stop(); ctx.close(); };
            function beep(freq, nextFreq) {
                if (!playing) return;
                osc.frequency.value = freq;
                setTimeout(function () {
                    if (!playing) return;
                    beep(nextFreq, freq);
                }, 500);
            }
            osc.start();
            beep(440, 550);
        } catch(e) { /* No audio context available */ }
    },

    stopRingtone: function () {
        if (this._ringtoneStop) {
            this._ringtoneStop();
            this._ringtoneStop = null;
        }
    },

    // ── Peer Connection ──

    /** Create a new RTCPeerConnection for a partner */
    createPeerConnection: function (partnerId) {
        var self = this;
        if (this._connections[partnerId]) {
            console.log("WebRTC: Connection already exists for " + partnerId);
            return;
        }

        var pc = new RTCPeerConnection(this._peerConfig);
        this._connections[partnerId] = pc;

        // Add local stream tracks
        if (this._localStream) {
            this._localStream.getTracks().forEach(function (track) {
                pc.addTrack(track, self._localStream);
            });
        }

        // ICE candidate → notify .NET
        pc.onicecandidate = function (evt) {
            if (evt.candidate) {
                console.log("WebRTC: New ICE candidate");
                self._dotNetRef.invokeMethodAsync('OnIceCandidate', partnerId, JSON.stringify(evt.candidate));
            } else {
                console.log("WebRTC: ICE gathering complete");
                self._dotNetRef.invokeMethodAsync('OnIceCandidate', partnerId, null);
            }
        };

        // Remote track → attach to audio element
        pc.ontrack = function (evt) {
            console.log("WebRTC: Remote track received");
            var audio = document.querySelector('.audio.partner');
            if (audio) {
                audio.srcObject = evt.streams[0];
                console.log("WebRTC: Attached remote stream to audio element");
            }
        };

        console.log("WebRTC: Created peer connection for " + partnerId);
    },

    /** Create an SDP offer */
    createOffer: function (partnerId) {
        var pc = this._connections[partnerId];
        if (!pc) throw new Error("No connection for " + partnerId);

        var self = this;
        return pc.createOffer()
            .then(function (offer) {
                return pc.setLocalDescription(offer);
            })
            .then(function () {
                console.log("WebRTC: Offer created");
                return JSON.stringify(pc.localDescription);
            });
    },

    /** Create an SDP answer */
    createAnswer: function (partnerId) {
        var pc = this._connections[partnerId];
        if (!pc) throw new Error("No connection for " + partnerId);

        var self = this;
        return pc.createAnswer()
            .then(function (answer) {
                return pc.setLocalDescription(answer);
            })
            .then(function () {
                console.log("WebRTC: Answer created");
                return JSON.stringify(pc.localDescription);
            });
    },

    /** Set remote SDP description */
    setRemoteDescription: function (partnerId, sdpJson) {
        var pc = this._connections[partnerId];
        if (!pc) throw new Error("No connection for " + partnerId);

        var desc = JSON.parse(sdpJson);
        return pc.setRemoteDescription(new RTCSessionDescription(desc))
            .then(function () {
                console.log("WebRTC: Remote description set (" + desc.type + ")");
            });
    },

    /** Add a remote ICE candidate */
    addIceCandidate: function (partnerId, candidateJson) {
        var pc = this._connections[partnerId];
        if (!pc) {
            console.warn("WebRTC: No connection for " + partnerId + ", skipping ICE candidate");
            return;
        }

        if (candidateJson) {
            var candidate = JSON.parse(candidateJson);
            pc.addIceCandidate(new RTCIceCandidate(candidate))
                .then(function () {
                    console.log("WebRTC: ICE candidate added");
                })
                .catch(function (err) {
                    console.error("WebRTC: Failed to add ICE candidate", err);
                });
        } else {
            // Null candidate signals end-of-candidates
            console.log("WebRTC: End of ICE candidates");
        }
    },

    /** Close a specific peer connection */
    closeConnection: function (partnerId) {
        var pc = this._connections[partnerId];
        if (pc) {
            pc.close();
            delete this._connections[partnerId];
            console.log("WebRTC: Closed connection for " + partnerId);
        }

        // Clear audio element
        var audio = document.querySelector('.audio.partner');
        if (audio) {
            audio.srcObject = null;
        }
    },

    /** Close all peer connections */
    closeAllConnections: function () {
        var self = this;
        Object.keys(this._connections).forEach(function (id) {
            self.closeConnection(id);
        });
    }
};
