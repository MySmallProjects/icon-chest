// boot.js - Blocking script in <head> to prevent theme flash before initial render and set up early error logging
(function() {
  function forwardError(message, stack) {
    try {
      if (window.external && typeof window.external.sendMessage === 'function') {
        window.external.sendMessage(JSON.stringify({
          type: 'logError',
          message: String(message),
          stack: stack ? String(stack) : null
        }));
      }
    } catch (_) {}

    try {
      fetch('/api/system/client-log', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: String(message), stack: stack ? String(stack) : null })
      }).catch(function() {});
    } catch (_) {}
  }

  window.addEventListener('error', function(event) {
    var msg = event.message || (event.error && event.error.message) || String(event);
    var stack = (event.error && event.error.stack) || (event.filename ? (event.filename + ':' + event.lineno + ':' + event.colno) : null);
    forwardError(msg, stack);
  });

  window.addEventListener('unhandledrejection', function(event) {
    var reason = event.reason;
    var msg = (reason && reason.message) ? reason.message : String(reason);
    var stack = (reason && reason.stack) ? reason.stack : null;
    forwardError('Unhandled Promise Rejection: ' + msg, stack);
  });

  try {
    var storedTheme = localStorage.getItem('iconchest_theme');
    var theme = storedTheme === 'light' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', theme);
    document.documentElement.style.colorScheme = theme;
  } catch (e) {
    // LocalStorage might be disabled in private/restricted modes; fallback to dark
    document.documentElement.setAttribute('data-theme', 'dark');
    document.documentElement.style.colorScheme = 'dark';
  }
})();

