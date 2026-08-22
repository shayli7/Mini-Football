// Proof pass for the exported set. Renders the real PNGs -- not the SVG source --
// so what gets checked is the file Unity will actually read.
//
//   node verify.mjs   ->  out/verify.png
import { writeFileSync, rmSync, readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { P } from './lib/palette.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'out');
const UNITY = join(HERE, '..', '..', 'Assets', 'Images', 'Icon');
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const b64 = f => `data:image/png;base64,${readFileSync(f).toString('base64')}`;
const FG = b64(join(UNITY, 'Icon_Adaptive_Foreground.png'));
const BG = b64(join(UNITY, 'Icon_Adaptive_Background.png'));
const ROUND = b64(join(UNITY, 'Icon_Round.png'));
const LEGACY = b64(join(UNITY, 'Icon_Legacy.png'));

// An adaptive icon: both layers stacked, then cropped by the launcher's mask to
// the centre 66.7%. Scaling by 1/0.667 and clipping reproduces exactly that.
const adaptive = (px, radius) => `
  <div style="width:${px}px;height:${px}px;overflow:hidden;border-radius:${radius};position:relative">
    <img src="${BG}" style="position:absolute;width:${px / 0.667}px;height:${px / 0.667}px;
         left:${-px * 0.25}px;top:${-px * 0.25}px">
    <img src="${FG}" style="position:absolute;width:${px / 0.667}px;height:${px / 0.667}px;
         left:${-px * 0.25}px;top:${-px * 0.25}px">
  </div>`;

const row = (label, inner) => `
  <div class="row"><span>${label}</span><div class="items">${inner}</div></div>`;

const html = `<!doctype html><meta charset="utf-8">
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600&display=swap" rel="stylesheet">
<style>
  body{margin:0;background:${P.bgDeep};color:${P.ink};font:400 14px/1.5 Inter,system-ui,sans-serif}
  .wrap{padding:36px 40px}
  h1{font:600 20px/1.2 Inter,sans-serif;margin:0 0 4px}
  .sub{color:${P.inkMuted};margin:0 0 28px;font-size:13px}
  .row{display:flex;align-items:center;gap:20px;padding:16px 0;border-top:1px solid ${P.line}}
  .row>span{width:210px;flex:0 0 auto;color:${P.inkMuted};font-size:12.5px}
  /* Pure black behind the tiles: the launcher case where a near-black ground
     would lose its edge. If the tile is visible here it is visible anywhere. */
  .items{display:flex;align-items:center;gap:22px;flex-wrap:wrap;
         background:#000;padding:18px 22px;border-radius:14px}
  .chk{background:
    linear-gradient(45deg,#FF00E0 25%,transparent 25%,transparent 75%,#FF00E0 75%),
    linear-gradient(45deg,#FF00E0 25%,#00E5FF 25%,#00E5FF 75%,#FF00E0 75%);
    background-size:24px 24px;background-position:0 0,12px 12px;padding:0;line-height:0}
  img{display:block}
</style>
<div class="wrap">
  <h1>Exported set — proof</h1>
  <p class="sub">Rendered from the PNGs in <code>Assets/Images/Icon/</code>, not from the SVG source.</p>

  ${row('Adaptive foreground<br>over magenta/cyan',
        `<div class="chk"><img src="${FG}" width="216" height="216"></div>
         <div class="chk"><img src="${FG}" width="108" height="108"></div>`)}

  ${row('Adaptive, launcher masks<br>circle · squircle · rounded',
        [['50%', 216], ['26%', 216], ['18%', 216]].map(([r, s]) => adaptive(s, r)).join('') )}

  ${row('Adaptive at real densities<br>108 · 72 · 48 px',
        [108, 72, 48].map(s => adaptive(s, '50%')).join(''))}

  ${row('Round PNG (512)',
        `<img src="${ROUND}" width="216" height="216"><img src="${ROUND}" width="72" height="72">
         <img src="${ROUND}" width="48" height="48">`)}

  ${row('Legacy PNG (512)',
        `<img src="${LEGACY}" width="216" height="216"><img src="${LEGACY}" width="72" height="72">
         <img src="${LEGACY}" width="48" height="48">`)}
</div>`;

const tmp = join(OUT, '.verify.html');
writeFileSync(tmp, html);
execFileSync(CHROME, ['--headless', '--disable-gpu', '--hide-scrollbars',
  '--force-device-scale-factor=1', '--window-size=1180,1180',
  `--screenshot=${join(OUT, 'verify.png').replace(/\\/g, '/')}`, tmp.replace(/\\/g, '/')],
  { stdio: 'pipe' });
rmSync(tmp, { force: true });
console.log('verify -> out/verify.png');
