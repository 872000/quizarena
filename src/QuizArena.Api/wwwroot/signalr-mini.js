// Minimal SignalR JSON-hub-protocol client over WebSocket. No dependencies.
// Implements just enough of the protocol for QuizArena:
// negotiate -> websocket -> handshake -> invoke/on.
(function (global) {
  const SEP = "\x1e";

  function createConnection(hubPath) {
    const handlers = {};
    let socket = null;
    let invocationId = 0;
    const pending = {};
    let connected = false;

    function send(obj) {
      socket.send(JSON.stringify(obj) + SEP);
    }

    function onMessage(event) {
      const parts = String(event.data).split(SEP);
      for (const part of parts) {
        if (!part) continue;
        let msg;
        try { msg = JSON.parse(part); } catch { continue; }
        if (msg.type === 3) { // Completion
          const cb = pending[msg.invocationId];
          delete pending[msg.invocationId];
          if (cb) {
            if (msg.error) cb.reject(new Error(msg.error));
            else cb.resolve(msg.result);
          }
        } else if (msg.type === 1 && !msg.invocationId) { // Server invocation
          const list = handlers[msg.target] || [];
          for (const h of list) {
            try { h.apply(null, msg.arguments || []); } catch (e) { console.error(e); }
          }
        } else if (msg.type === 7) { // Close
          if (socket) socket.close();
        }
      }
    }

    const connection = {
      on(method, handler) {
        (handlers[method] = handlers[method] || []).push(handler);
        return connection;
      },
      invoke(target) {
        const args = Array.prototype.slice.call(arguments, 1);
        return new Promise((resolve, reject) => {
          if (!connected) { reject(new Error("Not connected")); return; }
          const id = String(++invocationId);
          pending[id] = { resolve, reject };
          send({ type: 1, invocationId: id, target, arguments: args });
        });
      },
      async start() {
        const base = location.origin + hubPath;
        const negotiate = await fetch(base + "/negotiate?negotiateVersion=1", { method: "POST" });
        if (!negotiate.ok) throw new Error("Negotiate failed: " + negotiate.status);
        const info = await negotiate.json();
        const token = info.connectionToken || info.connectionId;
        const wsUrl = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + hubPath + "?id=" + encodeURIComponent(token);

        await new Promise((resolve, reject) => {
          socket = new WebSocket(wsUrl);
          socket.onopen = () => {
            socket.send(JSON.stringify({ protocol: "json", version: 1 }) + SEP);
          };
          socket.onmessage = (event) => {
            if (!connected) {
              // First message is the handshake response.
              connected = true;
              socket.onmessage = onMessage;
              resolve();
            }
          };
          socket.onerror = () => reject(new Error("WebSocket error"));
          socket.onclose = () => { connected = false; };
          setTimeout(() => { if (!connected) reject(new Error("Handshake timed out")); }, 8000);
        });
      }
    };

    return connection;
  }

  global.SignalRMini = { createConnection };
})(window);
