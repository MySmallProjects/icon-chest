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

  window.logClientError = forwardError;

  // Intercept console.error to ensure caught errors and warnings are forwarded to API logs
  var originalConsoleError = console.error;
  var lastSentMsg = '';
  var lastSentTime = 0;

  console.error = function() {
    originalConsoleError.apply(console, arguments);
    try {
      var args = Array.prototype.slice.call(arguments);
      var msg = args.map(function(a) {
        if (a instanceof Error) return a.message;
        if (typeof a === 'object') {
          try { return JSON.stringify(a); } catch(_) { return String(a); }
        }
        return String(a);
      }).join(' ');

      var errObj = null;
      for (var i = 0; i < args.length; i++) {
        if (args[i] instanceof Error) {
          errObj = args[i];
          break;
        }
      }
      var stack = errObj ? errObj.stack : null;

      // Throttle identical error messages within 1 second to avoid message spam
      var now = Date.now();
      if (msg === lastSentMsg && (now - lastSentTime < 1000)) {
        return;
      }
      lastSentMsg = msg;
      lastSentTime = now;

      forwardError(msg, stack);
    } catch (_) {}
  };

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

