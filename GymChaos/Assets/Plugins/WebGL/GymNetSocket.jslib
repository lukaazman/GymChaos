// Browser WebSocket for GymNetSocket (WebGL player). Messages are queued on
// the JS side and polled from C# each frame.
mergeInto(LibraryManager.library, {
  GymNetOpen: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (!Module.gymNet) { Module.gymNet = { next: 1, sockets: {} }; }
    var id = Module.gymNet.next++;
    var entry = { ws: null, inbox: [], state: 0 };
    Module.gymNet.sockets[id] = entry;
    try {
      entry.ws = new WebSocket(url);
      entry.ws.onopen = function () { entry.state = 1; };
      // A tab in the background queues messages: keep only the newest few hundred so catching up is not a long stall.
      entry.ws.onmessage = function (e) {
        if (typeof e.data !== 'string') { return; }
        entry.inbox.push(e.data);
        if (entry.inbox.length > 300) { entry.inbox.splice(0, entry.inbox.length - 300); }
      };
      entry.ws.onclose = function () { entry.state = 2; };
      entry.ws.onerror = function () { entry.state = 2; };
    } catch (e) { entry.state = 2; }
    return id;
  },
  // 0 connecting, 1 open, 2 closed or failed
  GymNetState: function (id) {
    var entry = Module.gymNet && Module.gymNet.sockets[id];
    return entry ? entry.state : 2;
  },
  GymNetSend: function (id, textPtr) {
    var entry = Module.gymNet && Module.gymNet.sockets[id];
    if (entry && entry.state === 1) { entry.ws.send(UTF8ToString(textPtr)); }
  },
  // Copies the next message into buffer; returns its byte length, 0 when empty, -1 when the buffer is too small.
  GymNetReceive: function (id, bufferPtr, bufferSize) {
    var entry = Module.gymNet && Module.gymNet.sockets[id];
    if (!entry || entry.inbox.length === 0) { return 0; }
    var text = entry.inbox[0];
    var needed = lengthBytesUTF8(text) + 1;
    if (needed > bufferSize) { return -1; }
    entry.inbox.shift();
    stringToUTF8(text, bufferPtr, bufferSize);
    return needed - 1;
  },
  GymNetClose: function (id) {
    var entry = Module.gymNet && Module.gymNet.sockets[id];
    if (entry && entry.ws) { entry.ws.close(); }
    if (entry) { entry.state = 2; delete Module.gymNet.sockets[id]; }
  }
});
