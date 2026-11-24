import { afterEach, describe, expect, it, vi } from 'vitest';
import { copyText } from '../src/utils/clipboard.js';

function setSecure(value) {
  Object.defineProperty(window, 'isSecureContext', { value, configurable: true });
}

function setClipboard(clipboard) {
  Object.defineProperty(navigator, 'clipboard', { value: clipboard, configurable: true });
}

describe('copyText', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    setClipboard(undefined);
    delete document.execCommand;
  });

  it('uses the Clipboard API in a secure context (https or localhost)', async () => {
    setSecure(true);
    const writeText = vi.fn().mockResolvedValue(undefined);
    setClipboard({ writeText });
    document.execCommand = vi.fn(() => true);
    await copyText('https://example.test');
    expect(writeText).toHaveBeenCalledWith('https://example.test');
    expect(document.execCommand).not.toHaveBeenCalled();
  });

  it('falls back to a selected textarea over plain http on another host', async () => {
    setSecure(false);
    setClipboard(undefined);
    let copied = null;
    document.execCommand = vi.fn(() => { copied = document.activeElement.value; return true; });
    await copyText('http://10.0.0.5:8080');
    expect(document.execCommand).toHaveBeenCalledWith('copy');
    expect(copied).toBe('http://10.0.0.5:8080');
    expect(document.querySelector('textarea')).toBeNull();
  });

  it('falls back when the Clipboard API rejects', async () => {
    setSecure(true);
    setClipboard({ writeText: vi.fn().mockRejectedValue(new Error('denied')) });
    document.execCommand = vi.fn(() => true);
    await copyText('value');
    expect(document.execCommand).toHaveBeenCalledWith('copy');
  });

  it('rejects when neither method works', async () => {
    setSecure(false);
    document.execCommand = vi.fn(() => false);
    await expect(copyText('value')).rejects.toThrow();
  });
});
