async function init() {
    const app = document.getElementById('app');

    try {
        const sessionRes = await fetch('/api/session');
        const session = await sessionRes.json();

        if (session && session.authenticated) {
            // Logged in: show system info and Swagger link
            const infoRes = await fetch('/api/system/info');
            const info = await infoRes.json();

            app.innerHTML = `
                <h1>IconChest System Info</h1>
                <p>Status: <strong>Authenticated</strong> (Mode: ${session.mode})</p>
                <p><a href="/swagger" target="_blank">Open Swagger UI &rarr;</a></p>
                <pre>${JSON.stringify(info, null, 2)}</pre>
                <button id="logoutBtn">Log Out</button>
            `;

            document.getElementById('logoutBtn').addEventListener('click', async () => {
                await fetch('/api/session', { method: 'DELETE' });
                window.location.reload();
            });
        } else {
            // Unauthenticated: show login form
            app.innerHTML = `
                <h1>IconChest Login</h1>
                <p>Enter the session secret or LAN password to log in.</p>
                <div id="errorMsg" class="error" style="display: none;"></div>
                <form id="loginForm">
                    <label for="credInput">Password or Secret:</label>
                    <input type="password" id="credInput" required autocomplete="current-password" />
                    <button type="submit">Log In</button>
                </form>
            `;

            document.getElementById('loginForm').addEventListener('submit', async (e) => {
                e.preventDefault();
                const cred = document.getElementById('credInput').value;
                const errDiv = document.getElementById('errorMsg');
                errDiv.style.display = 'none';

                // Try secret login first, fallback to password
                let res = await fetch('/api/session', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ secret: cred })
                });

                if (!res.ok) {
                    res = await fetch('/api/session', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ password: cred })
                    });
                }

                if (res.ok) {
                    window.location.reload();
                } else if (res.status === 429) {
                    const retryAfter = res.headers.get('Retry-After') || '60';
                    errDiv.textContent = `Too many failed login attempts. Please wait ${retryAfter} seconds.`;
                    errDiv.style.display = 'block';
                } else {
                    errDiv.textContent = 'Invalid secret or password.';
                    errDiv.style.display = 'block';
                }
            });
        }
    } catch (err) {
        app.innerHTML = `<h1>Error</h1><p>${err.message}</p>`;
    }
}

init();

