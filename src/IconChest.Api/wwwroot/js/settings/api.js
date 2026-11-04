import { el, clear } from '../lib/dom.js';
import { api } from '../lib/api.js';
import { toast } from '../lib/toast.js';
import { dialog } from '../lib/dialog.js';
import { bridge } from '../lib/bridge.js';
import { saveSetting, showRestartBanner } from './helper.js';

export function renderApiSection() {
  const container = el('div', { className: 'settings-section' });

  const header = el('div', { className: 'settings-header' },
    el('h2', {}, 'API & Network Access'),
    el('p', {}, 'Configure local port preferences, local network access, and remote authentication.')
  );
  container.appendChild(header);

  let hasLanPassword = false;
  let currentAllowLan = false;

  // 1. Port Configuration
  const portInput = el('input', {
    type: 'number',
    className: 'input',
    id: 'setting-port-preference',
    min: '0',
    max: '65535',
    style: { width: '140px' }
  });

  portInput.addEventListener('change', () => {
    let val = parseInt(portInput.value, 10);
    if (isNaN(val) || val < 0) val = 0;
    if (val > 65535) val = 65535;
    if (val > 0 && val < 1024) val = 1024;
    portInput.value = val;
    saveSetting({
      api: { portPreference: val }
    }).catch(() => {});
  });

  const currentPortSpan = el('span', { style: { fontWeight: '600' } }, 'Detecting...');

  const portCard = el('div', { className: 'card' },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Network Port'),
    el('div', { className: 'form-group' },
      el('label', {}, 'Active Listening Port'),
      el('div', { style: { display: 'flex', alignItems: 'center', gap: '8px' } },
        currentPortSpan
      ),
      el('div', { className: 'form-help' }, 'The port currently bound by the IconChest background service.')
    ),
    el('div', { className: 'form-group' },
      el('label', { htmlFor: 'setting-port-preference' }, 'Port Preference (0 = Dynamic/Random)'),
      el('div', { style: { display: 'flex', alignItems: 'center', gap: '8px' } },
        portInput,
        el('span', { style: { color: 'var(--text-muted)' } }, '(0 or 1024-65535)')
      ),
      el('div', { className: 'form-help' }, 'A fixed port preference requires restarting the application.')
    )
  );

  // 2. LAN Access & Password
  const allowLanCheckbox = el('input', { type: 'checkbox', id: 'setting-allow-lan' });
  const lanWarning = el('div', {
    className: 'banner banner-warning',
    style: { marginTop: '12px', display: 'none' }
  }, 'Warning: Local network HTTP traffic is unencrypted. Anyone on your local network can inspect traffic unless placed behind a reverse proxy with TLS.');

  const lanUrlsContainer = el('div', {
    style: {
      marginTop: '12px',
      padding: '12px',
      background: 'var(--bg-subtle, rgba(0,0,0,0.03))',
      borderRadius: 'var(--radius-md, 6px)',
      display: 'none'
    }
  });

  const lanPasswordStatusEl = el('span', { style: { fontSize: '0.9rem', color: 'var(--text-muted)' } });

  const btnSetPassword = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: () => promptPasswordModal()
  }, 'Set LAN Password...');

  allowLanCheckbox.addEventListener('change', async () => {
    if (allowLanCheckbox.checked && !hasLanPassword) {
      allowLanCheckbox.checked = false;
      toast.show('A LAN password must be configured before enabling LAN access.', 'warning');
      promptPasswordModal();
      return;
    }

    try {
      await saveSetting({
        api: { allowLan: allowLanCheckbox.checked }
      });
      currentAllowLan = allowLanCheckbox.checked;
      lanWarning.style.display = currentAllowLan ? 'block' : 'none';
      lanUrlsContainer.style.display = currentAllowLan ? 'block' : 'none';
      if (currentAllowLan) {
        refreshLanUrls();
      }
    } catch {
      allowLanCheckbox.checked = currentAllowLan;
    }
  });

  const lanCard = el('div', { className: 'card', style: { marginTop: '16px' } },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Local Area Network (LAN) Access'),
    el('div', { className: 'form-group' },
      el('label', { className: 'checkbox-label' },
        allowLanCheckbox,
        el('span', {}, 'Allow access from other devices on the local network')
      ),
      el('div', { className: 'form-help' }, 'Enables binding on all network interfaces rather than loopback only.')
    ),
    lanWarning,
    lanUrlsContainer,
    el('div', {
      style: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginTop: '16px',
        paddingTop: '16px',
        borderTop: '1px solid var(--border-subtle)'
      }
    },
      el('div', {},
        el('div', { style: { fontWeight: '500' } }, 'LAN Authentication Password'),
        lanPasswordStatusEl
      ),
      btnSetPassword
    )
  );

  // 3. Restart API Button
  const btnRestartApi = el('button', {
    type: 'button',
    className: 'btn btn-secondary',
    onClick: async () => {
      const ok = await dialog.confirm({
        title: 'Restart IconChest Service',
        message: 'Are you sure you want to restart the IconChest service process?',
        confirmText: 'Restart Now'
      });
      if (!ok) return;

      toast.show('Restarting service...', 'info');
      if (bridge.isAvailable()) {
        bridge.restartApi();
      } else {
        try {
          await api.post('/api/system/restart');
          toast.show('Service restarting...', 'info');
        } catch (err) {
          toast.show(err.message || 'Failed to trigger restart', 'error');
        }
      }
    }
  }, 'Restart API Service');

  const restartCard = el('div', { className: 'card', style: { marginTop: '16px' } },
    el('h3', { style: { marginBottom: '12px', fontSize: '1rem', fontWeight: '600' } }, 'Service Process Control'),
    el('p', { style: { color: 'var(--text-secondary)', marginBottom: '16px' } },
      'Gracefully restart the background API worker process to apply modified network port or binding settings.'
    ),
    btnRestartApi
  );

  container.appendChild(portCard);
  container.appendChild(lanCard);
  container.appendChild(restartCard);

  function promptPasswordModal() {
    const dialogElem = el('dialog', { className: 'app-dialog' });
    document.body.appendChild(dialogElem);

    const currentPwdInput = el('input', { type: 'password', className: 'input', placeholder: 'Current LAN password' });
    const newPwdInput = el('input', { type: 'password', className: 'input', placeholder: 'New password (min 8 chars)' });
    const confirmPwdInput = el('input', { type: 'password', className: 'input', placeholder: 'Confirm new password' });
    const errorMsg = el('div', { style: { color: 'var(--color-danger, #ef4444)', fontSize: '0.85rem', marginTop: '6px' } });

    const cancelBtn = el('button', {
      type: 'button',
      className: 'btn',
      onClick: () => {
        dialogElem.close();
        document.body.removeChild(dialogElem);
      }
    }, 'Cancel');

    const saveBtn = el('button', {
      type: 'button',
      className: 'btn btn-primary',
      onClick: async () => {
        errorMsg.textContent = '';
        const newPwd = newPwdInput.value;
        if (!newPwd || newPwd.length < 8) {
          errorMsg.textContent = 'Password must be at least 8 characters long.';
          return;
        }
        if (newPwd !== confirmPwdInput.value) {
          errorMsg.textContent = 'New passwords do not match.';
          return;
        }

        try {
          await api.request('/api/settings/lan-password', {
            method: 'PUT',
            body: {
              currentPassword: hasLanPassword ? currentPwdInput.value : null,
              newPassword: newPwd
            }
          });

          hasLanPassword = true;
          lanPasswordStatusEl.textContent = 'Password configured';
          btnSetPassword.textContent = 'Change Password...';
          allowLanCheckbox.disabled = false;
          dialogElem.close();
          document.body.removeChild(dialogElem);
          toast.show('LAN password updated successfully.', 'success');
        } catch (err) {
          errorMsg.textContent = err.detail || err.message || 'Failed to update password.';
        }
      }
    }, 'Save Password');

    const bodyChildren = [];
    if (hasLanPassword) {
      bodyChildren.push(
        el('div', { className: 'form-group' },
          el('label', {}, 'Current Password'),
          currentPwdInput
        )
      );
    }

    bodyChildren.push(
      el('div', { className: 'form-group' },
        el('label', {}, 'New Password'),
        newPwdInput
      ),
      el('div', { className: 'form-group' },
        el('label', {}, 'Confirm New Password'),
        confirmPwdInput
      ),
      errorMsg
    );

    const dialogContent = el('div', {},
      el('div', { className: 'dialog-header' }, hasLanPassword ? 'Change LAN Password' : 'Set LAN Password'),
      el('div', { className: 'dialog-body' }, ...bodyChildren),
      el('div', { className: 'dialog-footer' }, cancelBtn, saveBtn)
    );

    dialogElem.appendChild(dialogContent);
    dialogElem.showModal();
    (hasLanPassword ? currentPwdInput : newPwdInput).focus();
  }

  async function refreshLanUrls() {
    try {
      const info = await api.get('/api/system/info');
      clear(lanUrlsContainer);
      const urls = info.lanUrls || [];
      if (urls.length > 0) {
        lanUrlsContainer.appendChild(el('div', { style: { fontWeight: '600', marginBottom: '8px' } }, 'Reachable Network Addresses:'));
        const ul = el('ul', { style: { margin: 0, paddingLeft: '20px' } });
        for (const u of urls) {
          ul.appendChild(el('li', {},
            el('a', { href: u, target: '_blank', style: { fontFamily: 'var(--font-mono)' } }, u)
          ));
        }
        lanUrlsContainer.appendChild(ul);
      } else {
        lanUrlsContainer.textContent = 'No local network addresses discovered.';
      }
    } catch {
      lanUrlsContainer.textContent = 'Unable to query network addresses.';
    }
  }

  // Initial load
  api.get('/api/settings').then(s => {
    portInput.value = s.api?.portPreference ?? s.portPreference ?? 0;
    currentAllowLan = (s.api?.allowLan ?? s.allowLan) === true;
    hasLanPassword = s.hasLanPassword === true || s.api?.hasLanPassword === true;

    allowLanCheckbox.checked = currentAllowLan;
    allowLanCheckbox.disabled = !hasLanPassword;
    lanWarning.style.display = currentAllowLan ? 'block' : 'none';
    lanUrlsContainer.style.display = currentAllowLan ? 'block' : 'none';

    if (hasLanPassword) {
      lanPasswordStatusEl.textContent = 'Password configured';
      btnSetPassword.textContent = 'Change Password...';
    } else {
      lanPasswordStatusEl.textContent = 'No password configured (required for LAN access)';
      btnSetPassword.textContent = 'Set LAN Password...';
    }

    if (currentAllowLan) {
      refreshLanUrls();
    }
  });

  api.get('/api/system/info').then(info => {
    currentPortSpan.textContent = info.port ? `${info.port} (${info.host || '127.0.0.1'})` : 'Active';
    btnRestartApi.disabled = !info.canRestart && !bridge.isAvailable();
  }).catch(() => {
    currentPortSpan.textContent = 'Unknown';
  });

  return container;
}
