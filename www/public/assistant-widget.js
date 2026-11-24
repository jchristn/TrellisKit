/*
 * TrellisKit assistant widget loader (docs/AI_CONTRACT.md section 13.3). No framework.
 *
 * <script src="https://<web domain>/assistant-widget.js" data-assistant="ast_..." data-position="BottomRight"
 *   data-open="false" data-theme="auto" data-label="Ask a question" async></script>
 *
 * Injects a launcher button and an iframe pointing at /embed/assistants/{id} on the property's own
 * domain, so the host page never sees tokens or messages. postMessage traffic is limited to
 * tk-assistant:ready, open, close, resize, and unread, with exact origin checks.
 */
(function () {
  var script = document.currentScript;
  if (!script) return;
  var id = script.getAttribute('data-assistant');
  if (!id || !/^ast_[A-Za-z0-9_]+$/.test(id)) return;
  var origin = new URL(script.src).origin;
  var position = script.getAttribute('data-position') === 'BottomLeft' ? 'left' : 'right';
  var label = script.getAttribute('data-label') || 'Ask a question';
  var theme = script.getAttribute('data-theme') || 'auto';
  var open = script.getAttribute('data-open') === 'true';

  var button = document.createElement('button');
  button.type = 'button';
  button.textContent = label;
  button.setAttribute('aria-expanded', 'false');
  button.style.cssText = 'position:fixed;bottom:20px;' + position + ':20px;z-index:2147483000;padding:12px 18px;border-radius:24px;border:0;' +
    'font:600 14px system-ui,sans-serif;cursor:pointer;box-shadow:0 4px 16px rgba(0,0,0,.2);background:#1f2933;color:#fff;';

  var frame = document.createElement('iframe');
  frame.title = label;
  frame.src = origin + '/embed/assistants/' + encodeURIComponent(id) + '?theme=' + encodeURIComponent(theme);
  frame.setAttribute('allow', 'clipboard-write');
  frame.style.cssText = 'position:fixed;bottom:80px;' + position + ':20px;z-index:2147483000;width:min(380px,calc(100vw - 40px));' +
    'height:min(560px,calc(100vh - 120px));border:0;border-radius:12px;box-shadow:0 8px 32px rgba(0,0,0,.25);background:#fff;display:none;';

  function setOpen(value) {
    open = value;
    frame.style.display = value ? 'block' : 'none';
    button.setAttribute('aria-expanded', value ? 'true' : 'false');
    if (frame.contentWindow) frame.contentWindow.postMessage({ type: value ? 'tk-assistant:open' : 'tk-assistant:close' }, origin);
  }

  button.addEventListener('click', function () { setOpen(!open); });
  window.addEventListener('message', function (event) {
    if (event.origin !== origin || event.source !== frame.contentWindow || !event.data || typeof event.data.type !== 'string') return;
    if (event.data.type === 'tk-assistant:close') setOpen(false);
    else if (event.data.type === 'tk-assistant:open') setOpen(true);
    else if (event.data.type === 'tk-assistant:resize' && typeof event.data.height === 'number') frame.style.height = Math.max(200, Math.min(event.data.height, window.innerHeight - 120)) + 'px';
    else if (event.data.type === 'tk-assistant:unread' && typeof event.data.count === 'number') button.textContent = event.data.count > 0 ? label + ' (' + event.data.count + ')' : label;
  });

  function mount() {
    document.body.appendChild(frame);
    document.body.appendChild(button);
    if (open) setOpen(true);
  }
  if (document.body) mount();
  else document.addEventListener('DOMContentLoaded', mount);
})();
