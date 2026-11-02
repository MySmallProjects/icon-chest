# IconChest Front End & Native Web View Specifications

## 1. Native Web View Requirements

IconChest uses **Photino.NET** (version `4.0.16`, Apache-2.0) for desktop execution, which relies on the operating system's native web view engine:

* **Linux:** Uses WebKitGTK and GTK 3 (`libgtk-3-0`).
  * **Ubuntu 22.04 LTS / Linux Mint 21 (Jammy):**
    * Required package: `libwebkit2gtk-4.0-37` (WebKitGTK 2.36 - 2.44) and `libgtk-3-0`.
    * Install via:
      ```bash
      sudo apt update
      sudo apt install libwebkit2gtk-4.0-37 libgtk-3-0
      ```
  * **Ubuntu 24.04 LTS / Linux Mint 22 (Noble):**
    * Ubuntu 24.04 transitioned to `libwebkit2gtk-4.1-0` (libsoup3). Photino.NET 4.0.16 native Linux assets (`Photino.Native.so`) are dynamically linked against `libwebkit2gtk-4.0.so.37`.
    * To run with native window on Ubuntu 24.04, install the 4.0 runtime or pull the package from the Ubuntu 22.04 (jammy) security archive:
      ```bash
      sudo apt install libwebkit2gtk-4.0-37
      ```
    * **Browser Fallback:** If the WebKitGTK 4.0 runtime library is missing or cannot be installed, IconChest provides a built-in browser fallback:
      ```bash
      iconchest-desktop --browser
      ```
      This boots the API child process on an ephemeral port and opens the system default web browser automatically.

* **Windows:** Uses Microsoft Edge WebView2 Evergreen Runtime.
  * Windows 11 and recent builds of Windows 10 include the WebView2 Runtime pre-installed.
  * Detection: IconChest checks for the presence of the WebView2 Evergreen registry key:
    `SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`
    under `HKEY_LOCAL_MACHINE` and `HKEY_CURRENT_USER` (handling 64-bit WOW6432Node paths). If the `pv` version value is missing, empty, or `0.0.0.0`, the runtime is reported missing.
  * Download: [Microsoft WebView2 Evergreen Bootstrapper](https://go.microsoft.com/fwlink/p/?LinkId=2124703).
  * If missing, the app displays a clear guidance message and allows running with `--browser`.

---

## 2. Minimum Web Engine Baseline & Feature Compatibility

Based on WebKitGTK 2.36+ (Safari 15.4 engine baseline, ~early 2022) and modern WebView2 (Chromium 100+), the following web platform capabilities are verified safe for production use in IconChest:

| Web Feature | Supported | Notes |
| :--- | :--- | :--- |
| **ES6 Modules** (`import`, `export`) | Yes | Standard in all target engines. |
| **Dynamic `import()`** | Yes | Supported since WebKitGTK 2.24+ / Chrome 63+. |
| **CSS Custom Properties** (Variables) | Yes | Used extensively for Light/Dark themes and design tokens. |
| **CSS Grid & Flexbox** | Yes | Used for 3-column app shell and responsive layouts. |
| **Native `<dialog>` Element** | Yes | Supported in WebKitGTK 2.36+ (Safari 15.4) and Chromium 37+. Provides modal accessibility and focus trapping. |
| **`color-scheme` CSS Property** | Yes | Supported in WebKitGTK 2.36+ and Chromium 81+. Syncs scrollbars and native form elements with Light/Dark themes. |
| **Pointer Events API** | Yes | `pointerdown`, `pointermove`, `pointerup`, `setPointerCapture`, `releasePointerCapture`. Used for pane resizing. |
| **Fetch API & AbortController** | Yes | Full request cancellation and timeout support. |
| **Server-Sent Events (`EventSource`)**| Yes | Used for live status updates (`/api/events`). |
| **Clipboard API** | Yes | `navigator.clipboard.writeText` with fallback to `document.execCommand('copy')`. |
| **Web Storage (`localStorage`)** | Yes | Used for immediate theme boot-up and local pane state. |
| **Drag & Drop Events** | Yes | Handled globally to prevent dragging external files from causing navigation. |

### Features Intentionally Avoided
* CSS `@starting-style` and CSS Transitions on display changes (WebKitGTK 2.44+).
* Popover API (`popover` attribute) (WebKitGTK 2.42+).
* CSS masonry layout or CSS subgrid edge cases.
* Custom elements / shadow DOM (not needed; plain DOM helpers provide superior simplicity and security).

---

## 3. Security & Content Security Policy (CSP)

The front end strictly adheres to the server-enforced Content Security Policy:
* `default-src 'self'`: Only same-origin resources.
* `script-src 'self'`: No inline `<script>` tags without src, no `eval()`, no dynamic string execution (`new Function()`).
* `style-src 'self'`: External `.css` stylesheets only. No inline `<style>` tags or inline `style="..."` attributes on elements (style mutations must occur via CSS classes or the CSSOM).
* `img-src 'self' data:`: Local images, thumbnails, and inline SVG data URIs.
* `connect-src 'self'`: API endpoints and `/api/events` EventSource.

---

## 4. Cache Control & Asset Revalidation

All static front-end assets (`index.html`, `js/**`, `css/**`) are served with:
```http
Cache-Control: no-cache
```
accompanied by an `ETag`. The browser always validates whether the assets have changed before serving from local cache, guaranteeing that application upgrades immediately take effect without stale cached modules.

