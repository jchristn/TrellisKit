// Reads emails from a local Mailpit inbox (verification and password reset links).

import { setTimeout as sleep } from 'node:timers/promises';

export class Mailpit {
  constructor(baseUrl, log) {
    this.baseUrl = baseUrl ? baseUrl.replace(/\/+$/, '') : null;
    this.log = log;
  }

  get enabled() { return Boolean(this.baseUrl); }

  async available() {
    if (!this.baseUrl) return false;
    try {
      const res = await fetch(`${this.baseUrl}/api/v1/messages?limit=1`);
      return res.ok;
    } catch {
      return false;
    }
  }

  // Newest message to `address` whose text matches `pattern`; returns the first capture group.
  async findToken(address, pattern, { sinceMs = 0, timeoutMs = 30000 } = {}) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      const res = await fetch(`${this.baseUrl}/api/v1/search?query=${encodeURIComponent(`to:"${address}"`)}&limit=20`);
      if (res.ok) {
        const data = await res.json();
        for (const m of data.messages ?? []) {
          if (sinceMs && Date.parse(m.Created) < sinceMs - 5000) continue;
          const full = await (await fetch(`${this.baseUrl}/api/v1/message/${m.ID}`)).json();
          const match = pattern.exec(`${full.Text ?? ''}\n${full.HTML ?? ''}`);
          if (match) return match[1];
        }
      }
      await sleep(1000);
    }
    return null;
  }
}
