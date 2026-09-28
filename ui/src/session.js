// The axiom program runs while its UI is open. The UI pings it regularly and says goodbye when the window
// closes; a reload pings again right away, so it does not end the program.
const PING_EVERY_MS = 20000;

export function keepSessionAlive() {
  const ping = () => fetch('/api/session/ping', { method: 'POST' }).catch(() => {});
  ping();
  setInterval(ping, PING_EVERY_MS);
  window.addEventListener('pagehide', () => navigator.sendBeacon('/api/session/goodbye'));
}
