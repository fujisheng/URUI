// Run the shared author fixture in an actual browser, with trusted pointer and keyboard input.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn, execFileSync } from 'node:child_process';
import { readFile, writeFile, mkdir, mkdtemp, rm, open } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { createHash } from 'node:crypto';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const pkg = path.join(root, 'Packages/com.fujisheng.urui');
const browser = process.env.URUI_TEST_BROWSER || '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge';
const output = path.resolve(process.argv[2] || 'artifacts/urui-preview');
await mkdir(output, { recursive: true });
const profile = await mkdtemp(path.join(output, 'profile-'));
const routes = new Map([
  ['/', path.join(pkg, 'Tests/Runtime/Resources/URUI/Tests/SliderSkin.html')],
  ['/select', path.join(pkg, 'Tests/Runtime/Resources/URUI/Tests/SelectSkin.html')],
  ['/scroll', path.join(pkg, 'Tests/Runtime/Resources/URUI/Tests/ScrollSkin.html')],
  ['/other', path.join(pkg, 'Tests/Runtime/Resources/URUI/Tests/OtherControls.html')],
  ['/flex-space', path.join(pkg, 'Native~/tests/fixtures/flex-whitespace.html')],
  ['/urui/controls.css', path.join(pkg, 'Runtime/Resources/URUI/controls.css')],
  ['/urui/preview.js', path.join(pkg, 'Web~/preview.js')],
]);
const server = createServer(async (request, response) => {
  const file = routes.get(request.url);
  try {
    if (!file) throw new Error('Unknown resource');
    response.setHeader('Content-Type', file.endsWith('.css') ? 'text/css' : file.endsWith('.js') ? 'text/javascript' : 'text/html');
    response.end(await readFile(file));
  } catch { response.writeHead(404); response.end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const stderr = await open(path.join(output, 'browser.stderr.log'), 'w');
const child = spawn(browser, ['--headless', '--disable-gpu', '--no-first-run', '--hide-scrollbars',
  '--window-size=128,128', '--force-device-scale-factor=1', '--remote-debugging-port=0',
  '--user-data-dir=' + profile, 'about:blank'], { stdio: ['ignore', 'ignore', stderr.fd] });
let socket;
const rows = [];
const pixels = [];
try {
  let port;
  for (let i = 0; i < 100; i++) {
    try { port = (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0]; break; }
    catch { await new Promise(resolve => setTimeout(resolve, 100)); }
  }
  assert.ok(port, 'Browser must start its debugging endpoint');
  const pages = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
  socket = new WebSocket(pages.find(page => page.type === 'page').webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let serial = 0;
  const pending = new Map();
  socket.onmessage = ({ data }) => {
    const message = JSON.parse(data);
    const call = pending.get(message.id);
    if (!call) return;
    pending.delete(message.id);
    message.error ? call.reject(new Error(JSON.stringify(message.error))) : call.resolve(message.result);
  };
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++serial;
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async expression => {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    assert.ok(!result.exceptionDetails, JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  await send('Page.enable');
  const syntax = await evaluate(`[
    ['animation','none garbage',true],['animation','none 1s ease',true],['animation','none, flash 1s',true],
    ['animation','flash none',true],['animation','none none',true],['animation','none none garbage',false],['animation','none none none',false],
    ['animation','1s ease',true],['transition','1s ease',true],
    ['transition','none garbage',false],['transition','none 1s',true],['transition','none none',false],
    ['transition','opacity 0s',true],['transition','none, opacity 1s',false]
  ].map(([property,value,expected])=>({property,value,expected,actual:CSS.supports(property,value)}))`);
  for (const row of syntax) assert.equal(row.actual, row.expected, JSON.stringify(row));
  const noneMeaning = await evaluate(`(()=>{const e=document.createElement('div');e.style.animation='none garbage';return {name:e.style.animationName,fill:e.style.animationFillMode};})()`);
  assert.deepEqual(noneMeaning, { name: 'garbage', fill: 'none' });
  await send('Emulation.setDeviceMetricsOverride', { width: 128, height: 128, deviceScaleFactor: 1, mobile: false });
  await send('Emulation.setDefaultBackgroundColorOverride', { color: { r: 0, g: 0, b: 0, a: 1 } });
  const numberRows = [];
  const arrow = async (key, code) => {
    await send('Input.dispatchKeyEvent', { type: 'keyDown', key, code: key, windowsVirtualKeyCode: code });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', key, code: key, windowsVirtualKeyCode: code });
  };
  for (const [attributes, up, down] of [
    [{min:'2',max:'5',step:'2',value:'3'},4,2], [{min:'2',max:'5',step:'2',value:'4'},4,2],
    [{min:'-1',max:'1',step:'.25',value:'.5'},.75,.5],
    [{step:'any',value:'1.25'},2.25,1.25], [{min:'0',max:'10',step:'any',value:'1.25'},2.25,1.25],
    [{step:'bogus',value:'1.25'},2.25,1.25]
  ]) {
    await evaluate(`(()=>{document.body.innerHTML='';const input=document.createElement('input');input.type='number';
      for(const [key,value] of Object.entries(${JSON.stringify(attributes)}))input.setAttribute(key,value);
      document.body.append(input);input.focus();})()`);
    await arrow('ArrowUp', 38);
    const actualUp = await evaluate(`Number(document.querySelector('input').value)`);
    await arrow('ArrowDown', 40);
    const actualDown = await evaluate(`Number(document.querySelector('input').value)`);
    assert.equal(actualUp, up, JSON.stringify(attributes)); assert.equal(actualDown, down, JSON.stringify(attributes));
    assert.equal(await evaluate(`document.querySelector('input').getAttribute('value')`), attributes.value);
    numberRows.push({ attributes, up: actualUp, down: actualDown });
  }
  const liveNumber = await evaluate(`(()=>{const e=document.querySelector('input');e.setAttribute('step','2');e.value='4';e.stepUp();
    const first=e.value;e.setAttribute('value','100');const retained=e.value;e.stepUp();return {first,retained,last:e.value,authored:e.getAttribute('value')};})()`);
  assert.deepEqual(liveNumber, { first:'5.25', retained:'5.25', last:'6', authored:'100' });
  const capture = async (name, samples) => {
    const file = path.join(output, name + '.png');
    const result = await send('Page.captureScreenshot', { format: 'png' });
    await writeFile(file, Buffer.from(result.data, 'base64'));
    const colors = JSON.parse(execFileSync('python3', ['-c',
      'import json,sys;from PIL import Image;im=Image.open(sys.argv[1]).convert("RGBA");print(json.dumps([im.getpixel(tuple(p)) for p in json.loads(sys.argv[2])]))',
      file, JSON.stringify(samples.map(sample => sample.point))], { encoding: 'utf8' }));
    samples.forEach((sample, index) => {
      assert.ok(sample.rgb.every((channel, c) => Math.abs(channel - colors[index][c]) <= 2), name + ': ' + JSON.stringify(colors[index]));
      pixels.push({ name, ...sample, actual: colors[index] });
    });
  };
  await send('Page.navigate', { url: 'http://127.0.0.1:' + server.address().port + '/flex-space' });
  for (let i = 0; i < 100; i++) {
    if (await evaluate('location.pathname === "/flex-space" && document.readyState === "complete"')) break;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  const flexState = () => evaluate(`(()=>{const row=document.getElementById('row');return {first:document.getElementById('first').getBoundingClientRect().x,last:document.getElementById('last').getBoundingClientRect().x,nodes:row.childNodes.length};})()`);
  const flexWhitespace = { initial: await flexState() };
  assert.equal(flexWhitespace.initial.first, 0); assert.equal(flexWhitespace.initial.last, 20); assert.ok(flexWhitespace.initial.nodes > 2);
  await evaluate(`document.getElementById('row').firstChild.textContent='\u00a0'`);
  flexWhitespace.nonbreaking = await flexState(); assert.ok(flexWhitespace.nonbreaking.last > 20);
  await evaluate(`document.getElementById('row').firstChild.textContent=${JSON.stringify(' \t\r\n\f')}`);
  flexWhitespace.restored = await flexState(); assert.equal(flexWhitespace.restored.last, 20);
  await send('Page.navigate', { url: 'http://127.0.0.1:' + server.address().port + '/' });
  for (let i = 0; i < 100; i++) {
    if (await evaluate('location.pathname === "/" && location.protocol === "http:" && document.readyState === "complete"')) break;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  await evaluate('fetch("/urui/preview.js").then(response=>response.ok ? response.text() : "").then(source=>{(0,eval)(source);})');
  assert.equal(await evaluate('typeof URUI !== "undefined" && typeof URUI.preview?.refresh === "function"'), true,
    'The explicit URUI preview helper must load');
  await evaluate('globalThis.changeCount=0; input.addEventListener("change",()=>changeCount++);');
  const state = async name => {
    await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    const row = await evaluate(`(()=>{const host=document.getElementById('slider-host'),f=fill.getBoundingClientRect(),t=thumb.getBoundingClientRect();
      return {value:input.valueAsNumber,width:f.width,height:f.height,thumbX:t.x+t.width/2,thumbY:t.y+t.height/2,
        orientation:host.getAttribute('data-urui-orientation'),changes:changeCount};})()`);
    rows.push({ name, ...row });
    return row;
  };
  const initial = await state('initial');
  assert.equal(initial.width, 50); assert.equal(initial.thumbX, 64); assert.equal(initial.changes, 0);
  await evaluate('input.min="10invalid";input.max="30invalid";input.value="50";URUI.preview.refresh();');
  const invalid = await state('invalid-bounds');
  assert.equal(invalid.value, 50); assert.equal(invalid.width, 50);
  await evaluate('input.min="0";input.max="100";input.value="50";URUI.preview.refresh();');
  await evaluate('input.min="20"');
  assert.equal((await state('range-change')).width, 37.5);
  await evaluate('input.value="80"; URUI.preview.refresh(document.getElementById("slider-host"));');
  const silent = await state('silent-write');
  assert.equal(silent.width, 75); assert.equal(silent.changes, 0);
  await evaluate('input.min="0";input.value="50";URUI.preview.refresh();');
  const click = async (x, y) => {
    await send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', clickCount: 1 });
    await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', clickCount: 1 });
  };
  await click(89, 30);
  const pointer = await state('pointer');
  assert.equal(pointer.value, 75); assert.equal(pointer.width, 75); assert.equal(pointer.changes, 1);
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'ArrowRight', code: 'ArrowRight', windowsVirtualKeyCode: 39 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'ArrowRight', code: 'ArrowRight', windowsVirtualKeyCode: 39 });
  assert.equal((await state('keyboard')).value, 76);
  await evaluate('input.disabled=true');
  await click(34, 30);
  const disabled = await state('disabled');
  assert.equal(disabled.value, 76); assert.equal(disabled.changes, 2);
  await evaluate('input.disabled=false;input.value="75";input.setAttribute("data-urui-orientation","vertical");' +
    'document.getElementById("slider-host").style.width="44px";document.getElementById("slider-host").style.height="100px";URUI.preview.refresh();');
  const vertical = await state('vertical');
  assert.equal(vertical.height, 75); assert.equal(vertical.thumbY, 85); assert.equal(vertical.orientation, 'vertical');
  await click(34, 35);
  assert.equal((await state('vertical-pointer')).value, 25);
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'ArrowDown', code: 'ArrowDown', windowsVirtualKeyCode: 40 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'ArrowDown', code: 'ArrowDown', windowsVirtualKeyCode: 40 });
  assert.equal((await state('vertical-keyboard')).value, 26);
  await send('Page.captureScreenshot', { format: 'png' }).then(result => writeFile(path.join(output, 'slider.png'), Buffer.from(result.data, 'base64')));
  await send('Page.navigate', { url: 'http://127.0.0.1:' + server.address().port + '/select' });
  for (let i = 0; i < 100; i++) {
    if (await evaluate('location.pathname === "/select" && document.readyState === "complete"')) break;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  await evaluate('fetch("/urui/preview.js").then(response=>response.text()).then(source=>{(0,eval)(source);})');
  await evaluate('globalThis.changeCount=0;input.addEventListener("change",()=>changeCount++);');
  const selectState = async name => {
    await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    const row = await evaluate(`(()=>{const p=popup.getBoundingClientRect();return {value:input.value,label:value.textContent,
      count:popup.children.length,hidden:getComputedStyle(popup).display==='none',prototypeHidden:getComputedStyle(prototype).display==='none',
      expanded:trigger.getAttribute('aria-expanded'),disabled:trigger.disabled,changes:changeCount,x:p.x,y:p.y,width:p.width,height:p.height};})()`);
    rows.push({ name: 'select-' + name, ...row }); return row;
  };
  const selectInitial = await selectState('initial');
  assert.equal(selectInitial.label, 'A'); assert.equal(selectInitial.count, 3);
  assert.equal(selectInitial.hidden, true); assert.equal(selectInitial.prototypeHidden, true);
  await capture('select-closed', [{ point: [100, 16], rgb: [0, 0, 255] }, { point: [100, 52], rgb: [0, 0, 0] }]);
  await evaluate('input.value="c";URUI.preview.refresh();');
  const selectSilent = await selectState('silent');
  assert.equal(selectSilent.label, 'C'); assert.equal(selectSilent.changes, 0);
  await evaluate('popup.style.setProperty("--urui-select-fade","1")');
  await click(60, 26);
  const enteringOpacity = await evaluate(`(()=>{const animation=popup.getAnimations()[0];if(animation){animation.pause();animation.currentTime=0;}return Number(getComputedStyle(popup).opacity);})()`);
  assert.ok(enteringOpacity < .01, 'Default select starts transparent instead of flashing its final state.');
  assert.ok(Math.abs(await evaluate('popup.getAnimations()[0].currentTime=75;Number(getComputedStyle(popup).opacity)')-.5)<.01);
  await evaluate('popup.getAnimations()[0].finish()');
  await new Promise(resolve=>setTimeout(resolve,60));
  await click(60,26);
  assert.equal(await evaluate('getComputedStyle(popup).pointerEvents'),'none');
  assert.equal(await evaluate('getComputedStyle(popup).display'),'block');
  await click(60,26);
  await new Promise(resolve=>setTimeout(resolve,220));
  assert.equal(await evaluate('trigger.getAttribute("aria-expanded")'),'true');
  assert.equal(await evaluate('getComputedStyle(popup).display'),'block');
  await click(60,26);await new Promise(resolve=>setTimeout(resolve,220));
  await evaluate('popup.style.removeProperty("--urui-select-fade")');
  await click(60, 26);
  const opened = await selectState('open');
  assert.equal(opened.hidden, false); assert.equal(opened.expanded, 'true');
  assert.equal(opened.x, 10); assert.equal(opened.y, 42); assert.equal(opened.width, 100);
  await capture('select-open', [{ point: [100, 52], rgb: [255, 0, 0] }, { point: [100, 72], rgb: [0, 0, 255] }, { point: [100, 92], rgb: [0, 255, 0] }]);
  await click(60, 52);
  const chosen = await selectState('choose');
  assert.equal(chosen.value, 'a'); assert.equal(chosen.hidden, true); assert.equal(chosen.changes, 1);
  await capture('select-chosen', [{ point: [100, 92], rgb: [0, 0, 0] }]);
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'ArrowDown', code: 'ArrowDown', windowsVirtualKeyCode: 40 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'ArrowDown', code: 'ArrowDown', windowsVirtualKeyCode: 40 });
  const selectKey = await selectState('keyboard');
  assert.equal(selectKey.value, 'c'); assert.equal(selectKey.changes, 2);
  await evaluate('input.disabled=true');
  assert.equal((await selectState('disabled')).disabled, true);
  await evaluate('input.disabled=false;input.replaceChildren(new Option("D","D",true,true),new Option("E","E"));URUI.preview.refresh();');
  const replaced = await selectState('choices');
  assert.equal(replaced.label, 'D'); assert.equal(replaced.count, 2);
  await click(60, 26); await click(120, 120);
  assert.equal((await selectState('outside')).hidden, true);
  await evaluate('input.options[1].hidden=true;URUI.preview.refresh();trigger.focus();');
  assert.equal(await evaluate('getComputedStyle(popup.children[1]).display'), 'none');
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'ArrowDown', code: 'ArrowDown', windowsVirtualKeyCode: 40 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'ArrowDown', code: 'ArrowDown', windowsVirtualKeyCode: 40 });
  assert.equal((await selectState('hidden-option')).value, 'D');
  const clonedOptions = await evaluate(`(()=>{const clone=document.getElementById('select-host').cloneNode(true);document.body.append(clone);
    URUI.preview.refresh(clone);return clone.querySelector('.urui-select-options').children.length;})()`);
  assert.equal(clonedOptions, 2, 'Cloned skins must replace their copied generated rows.');
  await send('Page.captureScreenshot', { format: 'png' }).then(result => writeFile(path.join(output, 'select.png'), Buffer.from(result.data, 'base64')));
  await send('Page.navigate', { url: 'http://127.0.0.1:' + server.address().port + '/scroll' });
  for (let i = 0; i < 100; i++) {
    if (await evaluate('location.pathname === "/scroll" && document.readyState === "complete"')) break;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  await evaluate('fetch("/urui/preview.js").then(response=>response.text()).then(source=>{(0,eval)(source);})');
  const evalScroll = expression => evaluate('(()=>{const viewport=document.getElementById("viewport"),track=document.getElementById("track"),' +
    'thumb=document.getElementById("thumb"),content=document.getElementById("content"),first=document.getElementById("first"),second=document.getElementById("second");' + expression + '})()');
  const scrollState = async name => {
    await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    const row = await evalScroll(`const t=thumb.getBoundingClientRect(),r=track.getBoundingClientRect();return {
      offsetX:viewport.scrollLeft,offsetY:viewport.scrollTop,viewportWidth:viewport.clientWidth,viewportHeight:viewport.clientHeight,
      thumbWidth:t.width,thumbHeight:t.height,thumbX:t.x-r.x,thumbY:t.y-r.y,visible:getComputedStyle(track).visibility!=='hidden'};`);
    rows.push({ name: 'scroll-' + name, ...row }); return row;
  };
  const scrollInitial = await scrollState('initial');
  assert.equal(scrollInitial.viewportWidth, 100); assert.equal(scrollInitial.thumbHeight, 18);
  await capture('scroll-initial', [{ point: [115, 15], rgb: [255, 255, 255] }, { point: [115, 60], rgb: [0, 255, 0] }, { point: [20, 20], rgb: [255, 0, 0] }]);
  await evalScroll('viewport.scrollTop=160;URUI.preview.refresh();');
  assert.equal((await scrollState('offset')).thumbY, 31);
  await capture('scroll-offset', [{ point: [115, 45], rgb: [255, 255, 255] }, { point: [20, 20], rgb: [255, 0, 0] }, { point: [20, 80], rgb: [0, 0, 255] }]);
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: 115, y: 50, button: 'left', clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 115, y: 81, button: 'left', buttons: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: 115, y: 81, button: 'left', clickCount: 1 });
  assert.equal((await scrollState('drag')).offsetY, 320);
  await capture('scroll-end', [{ point: [115, 80], rgb: [255, 255, 255] }, { point: [20, 20], rgb: [0, 0, 255] }]);
  await evalScroll('viewport.scrollTop=0;URUI.preview.refresh();'); await scrollState('reset');
  await click(115, 60); assert.equal((await scrollState('page')).offsetY, 80);
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'End', code: 'End', windowsVirtualKeyCode: 35 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'End', code: 'End', windowsVirtualKeyCode: 35 });
  assert.equal((await scrollState('keyboard')).offsetY, 320);
  await evalScroll('thumb.style.fontSize="10px";thumb.style.minHeight="2em";URUI.preview.refresh();');
  assert.equal((await scrollState('minimum-em')).thumbHeight, 20);
  await evalScroll('thumb.style.minHeight="25%";URUI.preview.refresh();');
  assert.equal((await scrollState('minimum-percent')).thumbHeight, 20);
  await evalScroll('thumb.style.minHeight="auto";URUI.preview.refresh();');
  assert.equal((await scrollState('minimum-auto')).thumbHeight, 16);
  await evalScroll('content.style.height="40px";first.style.height="40px";second.style.height="0";URUI.preview.refresh();');
  assert.equal((await scrollState('no-overflow')).visible, false);
  await evalScroll('document.getElementById("frame").setAttribute("data-urui-orientation","horizontal");' +
    'document.getElementById("frame").style.width="80px";document.getElementById("frame").style.height="112px";' +
    'track.setAttribute("data-urui-orientation","horizontal");content.style.width="400px";content.style.height="100px";' +
    'first.style.width="400px";first.style.height="100px";viewport.scrollLeft=160;URUI.preview.refresh();');
  const horizontal = await scrollState('horizontal');
  assert.equal(horizontal.viewportWidth, 80); assert.equal(horizontal.viewportHeight, 100);
  assert.equal(horizontal.thumbWidth, 18); assert.equal(horizontal.thumbX, 31);
  await send('Input.dispatchMouseEvent', { type: 'mousePressed', x: 50, y: 115, button: 'left', clickCount: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 81, y: 115, button: 'left', buttons: 1 });
  await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: 81, y: 115, button: 'left', clickCount: 1 });
  assert.equal((await scrollState('horizontal-drag')).offsetX, 320);
  await send('Page.navigate', { url: 'http://127.0.0.1:' + server.address().port + '/other' });
  for (let i = 0; i < 100; i++) {
    if (await evaluate('location.pathname === "/other" && document.readyState === "complete"')) break;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  await evaluate('fetch("/urui/preview.js").then(response=>response.text()).then(source=>{(0,eval)(source);})');
  const evalOther = expression => evaluate('(()=>{const fold=document.getElementById("fold"),header=document.getElementById("header"),' +
    'content=document.getElementById("fold-content"),progress=document.getElementById("progress"),fill=document.getElementById("fill"),title=document.getElementById("title");' + expression + '})()');
  await evalOther('globalThis.foldChanges=0;fold.addEventListener("change",()=>foldChanges++);');
  const otherState = async name => {
    await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    const row = await evalOther(`return {expanded:header.getAttribute('aria-expanded'),hidden:getComputedStyle(content).display==='none',
      label:header.querySelector('.urui-foldout-label').textContent,fillWidth:fill.getBoundingClientRect().width,title:title.textContent,changes:foldChanges};`);
    rows.push({ name: 'other-' + name, ...row }); return row;
  };
  const otherInitial = await otherState('initial');
  assert.equal(otherInitial.fillWidth, 50); assert.equal(otherInitial.hidden, true); assert.equal(otherInitial.title, 'Loading');
  await capture('progress-initial', [{ point: [20, 91], rgb: [0, 255, 0] }, { point: [95, 91], rgb: [0, 0, 255] }]);
  await evalOther('header.focus();');
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'ArrowRight', code: 'ArrowRight', windowsVirtualKeyCode: 39 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'ArrowRight', code: 'ArrowRight', windowsVirtualKeyCode: 39 });
  const foldKey = await otherState('keyboard');
  assert.equal(foldKey.hidden, false); assert.equal(foldKey.expanded, 'true'); assert.equal(foldKey.changes, 1);
  await evalOther('fold.setAttribute("data-urui-expanded","false");');
  const foldSilent = await otherState('silent');
  assert.equal(foldSilent.hidden, true); assert.equal(foldSilent.changes, 1);
  await click(20, 20);
  const foldPointer = await otherState('pointer');
  assert.equal(foldPointer.hidden, false); assert.equal(foldPointer.changes, 2);
  await evalOther('fold.setAttribute("data-urui-label","Updated");');
  assert.equal((await otherState('label')).label, 'Updated');
  await evalOther('progress.setAttribute("value","25");');
  assert.equal((await otherState('progress-value')).fillWidth, 75);
  await capture('progress-updated', [{ point: [80, 91], rgb: [0, 255, 0] }]);
  await evalOther('progress.setAttribute("max","40");');
  assert.equal((await otherState('progress-range')).fillWidth, 50);
  await evalOther('progress.setAttribute("data-urui-label","Updated");');
  assert.equal((await otherState('progress-label')).title, 'Updated');
  const inputs = {};
  for (const file of routes.values()) inputs[path.relative(pkg, file)] = createHash('sha256').update(await readFile(file)).digest('hex');
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ browser: execFileSync(browser, ['--version'], { encoding: 'utf8' }).trim(),
    inputs, rows, pixels, syntax, numberRows, liveNumber, flexWhitespace, capture: { width: 128, height: 128, dpr: 1, clear: '#000000' },
    scope: 'URUI authored controls basic state/geometry/trusted input and seventeen flat primary-color samples only; not full T13 pixel acceptance.' }, null, 2) + '\n');
  console.log('Browser controls preview: ' + rows.length + ' states passed.');
} finally {
  socket?.close(); child.kill(); server.close(); await stderr.close();
  await new Promise(resolve => child.exitCode !== null ? resolve() : child.once('exit', resolve));
  await rm(profile, { recursive: true, force: true });
}
