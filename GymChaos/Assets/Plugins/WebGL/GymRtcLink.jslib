// Browser WebRTC data channel for GymRtcLink (WebGL player). Everything is queued on the JS side and
// polled from C# each frame, like GymNetSocket.jslib. Signals (offer, answer, ICE) are JSON texts the
// game swaps through the PeerJS broker.
mergeInto(LibraryManager.library, {
  GymRtcOpen: function (offering, stunPtr) {
    var stun = UTF8ToString(stunPtr).split(',');
    if (!Module.gymRtc) { Module.gymRtc = { next: 1, links: {} }; }
    var id = Module.gymRtc.next++;
    var entry = { pc: null, ch: null, state: 0, inbox: [], signals: [], early: [], remoteSet: false };
    Module.gymRtc.links[id] = entry;
    try {
      entry.pc = new RTCPeerConnection({ iceServers: [{ urls: stun }] });
      entry.pc.onicecandidate = function (e) {
        if (e.candidate) {
          entry.signals.push(JSON.stringify({ k: 'ice', c: e.candidate.candidate, mid: e.candidate.sdpMid || '', idx: e.candidate.sdpMLineIndex || 0 }));
        }
      };
      entry.pc.onconnectionstatechange = function () {
        var s = entry.pc.connectionState;
        if (s === 'failed' || s === 'closed') { entry.state = 2; }
      };
      var wire = function (ch) {
        entry.ch = ch;
        ch.onopen = function () { if (entry.state === 0) { entry.state = 1; } };
        ch.onclose = function () { entry.state = 2; };
        ch.onerror = function () { entry.state = 2; };
        ch.onmessage = function (e) {
          if (typeof e.data !== 'string') { return; }
          entry.inbox.push(e.data);
          // A tab in the background queues messages: keep only the newest few hundred so catching up is not a long stall.
          if (entry.inbox.length > 300) { entry.inbox.splice(0, entry.inbox.length - 300); }
        };
      };
      if (offering) {
        wire(entry.pc.createDataChannel('gym', { ordered: true }));
        entry.pc.createOffer()
          .then(function (offer) { return entry.pc.setLocalDescription(offer); })
          .then(function () {
            entry.signals.push(JSON.stringify({ k: 'sdp', type: entry.pc.localDescription.type, sdp: entry.pc.localDescription.sdp }));
          })
          .catch(function () { entry.state = 2; });
      } else {
        entry.pc.ondatachannel = function (e) { wire(e.channel); };
      }
    } catch (e) { entry.state = 2; }
    return id;
  },
  // 0 connecting, 1 open, 2 closed or failed
  GymRtcState: function (id) {
    var entry = Module.gymRtc && Module.gymRtc.links[id];
    return entry ? entry.state : 2;
  },
  GymRtcSend: function (id, textPtr) {
    var entry = Module.gymRtc && Module.gymRtc.links[id];
    if (entry && entry.state === 1 && entry.ch && entry.ch.readyState === 'open') { entry.ch.send(UTF8ToString(textPtr)); }
  },
  // Copies the next message into buffer; returns its byte length, 0 when empty, -1 when the buffer is too small.
  GymRtcReceive: function (id, bufferPtr, bufferSize) {
    var entry = Module.gymRtc && Module.gymRtc.links[id];
    if (!entry || entry.inbox.length === 0) { return 0; }
    var text = entry.inbox[0];
    var needed = lengthBytesUTF8(text) + 1;
    if (needed > bufferSize) { return -1; }
    entry.inbox.shift();
    stringToUTF8(text, bufferPtr, bufferSize);
    return needed - 1;
  },
  // The other end's offer, answer or ICE candidate.
  GymRtcSignalIn: function (id, jsonPtr) {
    var entry = Module.gymRtc && Module.gymRtc.links[id];
    if (!entry || !entry.pc) { return; }
    var signal;
    try { signal = JSON.parse(UTF8ToString(jsonPtr)); } catch (e) { return; }
    var pc = entry.pc;
    if (signal.k === 'sdp') {
      pc.setRemoteDescription({ type: signal.type, sdp: signal.sdp })
        .then(function () {
          entry.remoteSet = true;
          entry.early.forEach(function (c) { pc.addIceCandidate(c).catch(function () {}); });
          entry.early = [];
          if (signal.type === 'offer') {
            return pc.createAnswer()
              .then(function (answer) { return pc.setLocalDescription(answer); })
              .then(function () { entry.signals.push(JSON.stringify({ k: 'sdp', type: pc.localDescription.type, sdp: pc.localDescription.sdp })); });
          }
        })
        .catch(function () { entry.state = 2; });
    } else if (signal.k === 'ice') {
      var candidate = { candidate: signal.c, sdpMid: signal.mid, sdpMLineIndex: signal.idx };
      if (entry.remoteSet) { pc.addIceCandidate(candidate).catch(function () {}); } else { entry.early.push(candidate); }
    }
  },
  // The next signal this end produced; same return convention as GymRtcReceive.
  GymRtcSignalOut: function (id, bufferPtr, bufferSize) {
    var entry = Module.gymRtc && Module.gymRtc.links[id];
    if (!entry || entry.signals.length === 0) { return 0; }
    var text = entry.signals[0];
    var needed = lengthBytesUTF8(text) + 1;
    if (needed > bufferSize) { return -1; }
    entry.signals.shift();
    stringToUTF8(text, bufferPtr, bufferSize);
    return needed - 1;
  },
  GymRtcClose: function (id) {
    var entry = Module.gymRtc && Module.gymRtc.links[id];
    if (!entry) { return; }
    entry.state = 2;
    try { if (entry.ch) { entry.ch.close(); } if (entry.pc) { entry.pc.close(); } } catch (e) {}
    delete Module.gymRtc.links[id];
  }
});
