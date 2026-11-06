// The browser half of the rendering benchmark: opens the game in a real Chrome window (Playwright, the
// installed Chrome, not a downloaded one), loads the recorded stress save, plays it at 1x and times each
// frame at play zoom and zoomed right out. Needs the dev server (npm run dev) and the harness's save.
// Run: node experiments/render-bench/browser-bench.mjs [scenario]   -> experiments/render-bench/out/browser-<scenario>.json
import { chromium } from 'playwright-core';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';

const scenario = process.argv[2] ?? 'city-1024';
const meta = JSON.parse(readFileSync(`experiments/render-bench/data/${scenario}.json`, 'utf8'));
const browser = await chromium.launch({ channel: 'chrome', headless: false, args: ['--window-size=1300,820'] });
const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
await page.goto('http://localhost:5173/');
await page.waitForFunction(() => window.view && typeof window.view.loadSave === 'function');
await page.evaluate(async (name) => {
  const text = await (await fetch(`/experiments/render-bench/data/${name}.save.json`)).text();
  if (!window.view.loadSave(text)) throw new Error('save did not load');
}, scenario);

const stats = (xs) => {
  const a = [...xs].sort((p, q) => p - q);
  const mean = a.reduce((s, v) => s + v, 0) / a.length;
  return { mean: +mean.toFixed(2), p50: +a[Math.floor(a.length / 2)].toFixed(2), p99: +a[Math.floor(a.length * 0.99)].toFixed(2), max: +a.at(-1).toFixed(2) };
};
const out = { scenario, viewport: '1280x720', movers: meta.mostMovers, views: {} };
for (const [name, zoom] of [['play', 1], ['zoomedOut', 0.25]]) {
  const samples = await page.evaluate(async ({ x, y, zoom }) => {
    window.view.look(x, y, zoom);
    window.view.run(1);
    await new Promise((r) => setTimeout(r, 2000));
    window.view.frameSamples();
    await new Promise((r) => setTimeout(r, 8000));
    return window.view.frameSamples();
  }, { x: meta.home.x, y: meta.home.y, zoom });
  const total = samples.reduce((s, f) => s + f.frame, 0);
  out.views[name] = {
    zoom,
    frames: samples.length,
    fps: +((samples.length * 1000) / total).toFixed(1),
    draw: stats(samples.map((f) => f.draw)),
    hud: stats(samples.map((f) => f.hud)),
    sim: stats(samples.map((f) => f.sim)),
    frame: stats(samples.map((f) => f.frame)),
  };
}
await page.screenshot({ path: `experiments/render-bench/out/browser-${scenario}.png` }).catch(() => {});
mkdirSync('experiments/render-bench/out', { recursive: true });
writeFileSync(`experiments/render-bench/out/browser-${scenario}.json`, JSON.stringify(out, null, 2));
console.log(JSON.stringify(out, null, 2));
await browser.close();
