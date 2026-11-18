// 使用已安装浏览器和 Node 内置 CDP 连接；探针在作者文档之外，不改写 HTML/CSS。
import assert from 'node:assert/strict';
import { spawn, execFileSync } from 'node:child_process';
import { readFile, writeFile, mkdir, mkdtemp, rm, open } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';

const [manifestPath, baseUrl, outputPath] = process.argv.slice(2);
if (!manifestPath || !baseUrl || !outputPath) throw new Error('Usage: capture_browser.mjs <inputs.json> <base-url> <output>');
const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const output = path.resolve(outputPath);
const browser = process.env.URUI_TEST_BROWSER || '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge';
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
await mkdir(output, { recursive: true });
const profile = await mkdtemp(path.join(output, 'browser-profile-'));
const stderr = await open(path.join(output, 'browser.stderr.log'), 'w');
const child = spawn(browser, ['--headless', '--disable-gpu', '--no-first-run', '--hide-scrollbars',
  '--force-device-scale-factor=1', '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'],
  { stdio: ['ignore', 'ignore', stderr.fd] });
let socket;
try {
  let port;
  for (let i = 0; i < 100; i++) {
    try { port = (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
    catch { await new Promise(resolve => setTimeout(resolve, 100)); }
  }
  assert.ok(port, 'Browser debugging endpoint unavailable');
  const pages = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
  socket = new WebSocket(pages.find(page => page.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let serial = 0;
  const pending = new Map();
  socket.onmessage = ({ data }) => {
    const message = JSON.parse(data), call = pending.get(message.id);
    if (!call) return;
    pending.delete(message.id); clearTimeout(call.timer);
    message.error ? call.reject(new Error(JSON.stringify(message.error))) : call.resolve(message.result);
  };
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++serial;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 30000);
    pending.set(id, { resolve, reject, timer }); socket.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async expression => {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    assert.ok(!result.exceptionDetails, JSON.stringify(result.exceptionDetails)); return result.result.value;
  };
  await send('Page.enable'); await send('Network.enable'); await send('Network.setCacheDisabled', { cacheDisabled: true });
  const preview = await readFile(manifest.previewPath, 'utf8');
  assert.equal(hash(preview), manifest.previewSha256);
  const version = execFileSync(browser, ['--version'], { encoding: 'utf8' }).trim();
  let count = 0;
  for (const viewport of manifest.viewports) {
    await send('Emulation.setDeviceMetricsOverride', { ...viewport, deviceScaleFactor: 1, mobile: false });
    for (const fixture of manifest.cases) {
      const inputs = {}, fonts = {};
      for (const resource of fixture.resources) {
        const response = await fetch(new URL(resource.uri, baseUrl)); assert.ok(response.ok, resource.uri);
        const served = new Uint8Array(await response.arrayBuffer()); assert.equal(hash(served), resource.sha256, resource.uri);
        inputs[resource.uri] = resource.sha256;
        if (resource.font) fonts[resource.uri] = resource.sha256;
      }
      const url = new URL(fixture.uri, baseUrl).href;
      await send('Page.navigate', { url });
      let ready = false;
      for (let i = 0; i < 200; i++) {
        ready = await evaluate(`location.href === ${JSON.stringify(url)} && document.readyState === 'complete'`);
        if (ready) break;
        await new Promise(resolve => setTimeout(resolve, 25));
      }
      assert.ok(ready, fixture.name + ' did not finish loading');
      await evaluate(`(async()=>{await Promise.all([...document.fonts].map(face => document.fonts.load(face.style+' '+face.weight+' 16px '+face.family)));await document.fonts.ready;return true;})()`);
      if (fixture.preview) await evaluate(preview);
      await evaluate(`(async()=>{for(const a of document.getAnimations()){a.pause();a.currentTime=0;}await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));return true;})()`);
      const snapshot = await evaluate('(' + collect.toString() + ')()');
      assert.equal(snapshot.viewport.width, viewport.width); assert.equal(snapshot.viewport.height, viewport.height);
      assert.ok(Object.keys(snapshot.nodes).length, 'No critical nodes');
      const directory = path.join(output, fixture.name + '-' + viewport.width + 'x' + viewport.height);
      await mkdir(directory, { recursive: true });
      const screenshot = Buffer.from((await send('Page.captureScreenshot', { format: 'png', fromSurface: true,
        captureBeyondViewport: false, clip: { x: 0, y: 0, ...viewport, scale: 1 } })).data, 'base64');
      await writeFile(path.join(directory, 'browser.png'), screenshot);
      Object.assign(snapshot, { schemaVersion: 1, runtime: 'browser', time: 0, inputs, fonts, screenshot: 'browser.png',
        screenshotSha256: hash(screenshot), browser: version, flatRegions: Object.fromEntries(fixture.regions.map(r => [r.name, r.rect])),
        baselineMethod: 'DOM Range bounds + Canvas fontBoundingBoxAscent',
        textBounds: 'CSS line-height boxes; renderer baseline and actual advances',
        extensions: fixture.preview ? 'authored-control-preview-fallback' : 'none',
        previewSha256: fixture.preview ? manifest.previewSha256 : null });
      await writeFile(path.join(directory, 'browser.json'), JSON.stringify(snapshot, null, 2) + '\n');
      count++;
    }
  }
  process.stdout.write(JSON.stringify({ captures: count, browser: version }) + '\n');
} finally {
  if (socket) socket.close();
  child.kill('SIGTERM');
  await Promise.race([new Promise(resolve => child.once('close', resolve)), new Promise(resolve => setTimeout(resolve, 3000))]);
  if (child.exitCode === null) child.kill('SIGKILL');
  await stderr.close(); await rm(profile, { recursive: true, force: true });
}

function collect() {
  const nodes = {}, canvas = document.createElement('canvas'), context = canvas.getContext('2d');
  for (const element of document.querySelectorAll('[data-test]')) {
    const name = element.getAttribute('data-test'), box = element.getBoundingClientRect(), style = getComputedStyle(element);
    if (name in nodes) throw new Error('Duplicate critical node: ' + name);
    let parent = element.parentElement;
    while (parent && !parent.hasAttribute('data-test')) parent = parent.parentElement;
    const lines = [], walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
    let text;
    while ((text = walker.nextNode())) {
      const font = getComputedStyle(text.parentElement);
      context.font = font.fontStyle + ' ' + font.fontWeight + ' ' + font.fontSize + ' ' + font.fontFamily;
      const metrics = context.measureText('Hg');
      if (!Number.isFinite(metrics.fontBoundingBoxAscent)) throw new Error('Font baseline metrics unavailable');
      const range = document.createRange(), textLines = []; let offset = 0, line;
      for (const character of text.textContent) {
        range.setStart(text, offset); offset += character.length; range.setEnd(text, offset);
        const r = range.getBoundingClientRect();
        if (r.width <= .01 || r.height <= 0) continue;
        if (!line || Math.abs(line.rect[1] - r.y) > .1) {
          line = { rect: [r.x, r.y, r.width, r.height], baseline: r.y + metrics.fontBoundingBoxAscent };
          textLines.push(line);
        } else {
          const end = Math.max(line.rect[0] + line.rect[2], r.right);
          line.rect[0] = Math.min(line.rect[0], r.x); line.rect[2] = end - line.rect[0];
          line.rect[3] = Math.max(line.rect[3], r.height);
        }
      }
      for (const line of textLines) {
        const height = parseFloat(font.lineHeight);
        if (!Number.isFinite(height)) throw new Error('Explicit line-height required by comparison fixtures');
        line.rect[1] -= (height - line.rect[3]) / 2; line.rect[3] = height; lines.push(line);
      }
    }
    nodes[name] = { tag: element.tagName.toLowerCase(), parent: parent?.getAttribute('data-test') ?? null,
      rect: [box.x, box.y, box.width, box.height], text: element.textContent, lines,
      styles: { 'font-size': parseFloat(style.fontSize), opacity: parseFloat(style.opacity), display: style.display, visibility: style.visibility } };
  }
  return { viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio }, nodes,
    fontsLoaded: [...document.fonts].every(face => face.status === 'loaded') };
}
