// boot.js - Blocking script in <head> to prevent theme flash before initial render
(function() {
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

