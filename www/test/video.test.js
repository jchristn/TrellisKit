import { describe, expect, it, vi } from 'vitest';
import { attachVideoSource } from '../src/components/feed/VideoPlayer.jsx';

function fakeHls({ supported = true } = {}) {
  const instances = [];
  class Hls {
    static isSupported() { return supported; }
    constructor(config) { this.config = config; this.handlers = {}; instances.push(this); }
    on(event, fn) { this.handlers[event] = fn; }
    loadSource(src) { this.src = src; }
    attachMedia(el) { this.el = el; }
    startLoad() { this.restarted = true; }
    destroy() { this.destroyed = true; }
  }
  Hls.Events = { ERROR: 'hlsError' };
  return { load: async () => ({ default: Hls }), instances };
}

describe('video playback source', () => {
  it('uses native HLS when the browser supports it (Safari)', () => {
    const el = { canPlayType: () => 'maybe', src: '' };
    attachVideoSource(el, { hlsUrl: '/api/v1/media/files/med_1/master.m3u8' }, vi.fn(), () => { throw new Error('hls.js must not load'); });
    expect(el.src).toBe('/api/v1/media/files/med_1/master.m3u8');
  });

  it('loads hls.js with credentials elsewhere and cleans up', async () => {
    const el = { canPlayType: () => '', src: '' };
    const hls = fakeHls();
    const cleanup = attachVideoSource(el, { hlsUrl: 'https://media.example.test/video/med_1/master.m3u8' }, vi.fn(), hls.load);
    await Promise.resolve();
    await Promise.resolve();
    const instance = hls.instances[0];
    const xhr = {};
    instance.config.xhrSetup(xhr);
    expect(xhr.withCredentials).toBe(true);
    expect(instance.src).toBe('https://media.example.test/video/med_1/master.m3u8');
    expect(instance.el).toBe(el);
    cleanup();
    expect(instance.destroyed).toBe(true);
  });

  it('falls back to the MP4 in passthrough mode and reports unsupported playback', async () => {
    const el = { canPlayType: () => '', src: '' };
    attachVideoSource(el, { hlsUrl: null, mp4Url: '/api/v1/media/files/med_1/original.mp4' }, vi.fn());
    expect(el.src).toBe('/api/v1/media/files/med_1/original.mp4');
    const onError = vi.fn();
    attachVideoSource({ canPlayType: () => '' }, { hlsUrl: '/x.m3u8' }, onError, fakeHls({ supported: false }).load);
    await new Promise((r) => setTimeout(r, 0));
    expect(onError).toHaveBeenCalledWith(true);
  });
});
