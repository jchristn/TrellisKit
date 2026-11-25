// Public galleries load the member app's lightweight HLS engine only when native playback needs it.
async function enablePlayer(video) {
  const url = video.dataset.hls;
  if (!url || video.canPlayType('application/vnd.apple.mpegurl') || video.getAttribute('src') !== url) return;
  try {
    const { default: Hls } = await import('hls.js/light');
    if (!Hls.isSupported()) return;
    const hls = new Hls({ autoStartLoad: false });
    hls.loadSource(url);
    hls.attachMedia(video);
    video.addEventListener('play', () => hls.startLoad(), { once: true });
    window.addEventListener('pagehide', () => hls.destroy(), { once: true });
  } catch {
    // Keep the native source available if the optional player cannot load.
  }
}

for (const video of document.querySelectorAll('video[data-hls]')) enablePlayer(video);
