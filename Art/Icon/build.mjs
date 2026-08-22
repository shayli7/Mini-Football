// MINI FOOTBALL app icon build.
//
//   node build.mjs sheet          -> out/sheet.html + out/sheet.png (all five concepts)
//   node build.mjs ship <id>      -> the full Android + Play Store export set
//
// Rasterising goes through headless Chrome because it is the one renderer already
// on this machine that produces an exact pixel size with a transparent background.
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { CONCEPTS, GLYPHS, ALL, byId, icon } from './lib/concepts.mjs';
import { P, D } from './lib/palette.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, 'out');
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const win = p => p.replace(/\\/g, '/');

// Placeholder store copy, written here and nowhere else. Not approved product
// copy -- swap it before the listing goes live.
const TAGLINE = 'Rods in your thumbs. Table in your pocket.';

/** Render an HTML string to a PNG of exactly w x h device pixels. */
function shoot(html, w, h, dest, { transparent = false } = {}) {
  const tmp = join(OUT, `.shot-${Date.now()}-${Math.random().toString(36).slice(2)}.html`);
  writeFileSync(tmp, `<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;padding:0;background:${transparent ? 'transparent' : P.bgDeep};}</style>${html}`);
  const args = [
    '--headless', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
    `--window-size=${w},${h}`, `--screenshot=${win(dest)}`,
  ];
  if (transparent) args.push('--default-background-color=00000000');
  args.push(win(tmp));
  execFileSync(CHROME, args, { stdio: 'pipe' });
  rmSync(tmp, { force: true });
  return dest;
}

/** An SVG string wrapped so it fills the whole viewport exactly. */
const page = svg => `<div style="width:100vw;height:100vh;line-height:0">${
  svg.replace(/width="\d+" height="\d+"/, 'width="100%" height="100%"')}</div>`;

// ---------------------------------------------------------------- contact sheet
function sheet(items = CONCEPTS, name = 'sheet', sub = 'Five directions, drawn in the Arcade Neon palette.', h = 2050) {
  mkdirSync(OUT, { recursive: true });

  const tile = (c, size, mask) =>
    `<div class="tile" style="width:${size}px;height:${size}px">${
      icon(c, { size, mask })}</div>`;

  const cards = items.map((c, i) => `
    <section class="card">
      <div class="hero">${tile(c, 300, 'squircle')}</div>
      <div class="meta">
        <h2>${i + 1}. ${c.name}</h2>
        <p>${c.line}</p>
      </div>
      <div class="masks">
        ${tile(c, 84, 'circle')}${tile(c, 84, 'squircle')}${tile(c, 84, 'square')}
        <span class="rule"></span>
        <div class="small">${tile(c, 56, 'squircle')}${tile(c, 40, 'squircle')}${tile(c, 28, 'squircle')}</div>
      </div>
    </section>`).join('');

  const strip = (bg, label) => `
    <div class="strip" style="background:${bg}">
      <span class="striplabel" style="color:${bg === '#0A0E14' ? '#8A97A8' : '#5B6472'}">${label}</span>
      ${items.map(c => `<div class="hs">${tile(c, 68, 'squircle')}<b style="color:${
        bg === '#0A0E14' ? '#EAF0F7' : '#1B2330'}">Mini Football</b></div>`).join('')}
    </div>`;

  const html = `<!doctype html><meta charset="utf-8"><title>MINI FOOTBALL app icon</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Oswald:wght@500;600&family=Inter:wght@400;500;600&display=swap" rel="stylesheet">
<style>
  :root{ --deep:${P.bgDeep}; --panel:${P.bgPanel}; --raised:${P.bgRaised};
         --line:${P.line}; --ink:${P.ink}; --muted:${P.inkMuted}; --gold:${P.gold}; }
  *{box-sizing:border-box}
  body{margin:0;background:var(--deep);color:var(--ink);
       font:400 15px/1.55 Inter,system-ui,sans-serif;
       background-image:radial-gradient(120% 80% at 50% 0%, #16202D 0%, ${P.bgDeep} 62%);}
  .wrap{max-width:1180px;margin:0 auto;padding:56px 32px 72px}
  header{display:flex;align-items:baseline;gap:16px;flex-wrap:wrap;
         padding-bottom:22px;border-bottom:1px solid var(--line);margin-bottom:40px}
  h1{font:600 30px/1.1 Oswald,system-ui,sans-serif;letter-spacing:.06em;text-transform:uppercase;margin:0}
  h1 em{font-style:normal;color:var(--gold)}
  header p{margin:0;color:var(--muted);font-size:14px}
  .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));gap:20px}
  .card{background:linear-gradient(180deg,var(--panel),#101722);border:1px solid var(--line);
        border-radius:22px;padding:24px;display:flex;flex-direction:column;gap:18px;
        box-shadow:0 18px 44px -22px #000;}
  .hero{display:flex;justify-content:center}
  .tile{display:inline-block;border-radius:0;flex:0 0 auto;
        filter:drop-shadow(0 10px 22px rgba(0,0,0,.55))}
  .tile svg{display:block}
  .meta h2{font:600 17px/1.2 Oswald,sans-serif;letter-spacing:.08em;text-transform:uppercase;margin:0 0 6px}
  .meta p{margin:0;color:var(--muted);font-size:13.5px}
  .masks{display:flex;align-items:center;gap:12px;padding-top:16px;border-top:1px solid var(--line)}
  .rule{flex:1}
  .small{display:flex;align-items:center;gap:10px}
  .strips{margin-top:44px;display:grid;gap:14px}
  .strip{display:flex;align-items:flex-start;gap:26px;padding:20px 24px;border-radius:18px;
         border:1px solid var(--line)}
  .striplabel{font:600 11px/1 Inter,sans-serif;letter-spacing:.14em;text-transform:uppercase;
              writing-mode:vertical-rl;transform:rotate(180deg);margin-right:4px}
  .hs{display:flex;flex-direction:column;align-items:center;gap:7px;width:92px}
  .hs b{font:500 10.5px/1.2 Inter,sans-serif;letter-spacing:.01em}
</style>
<div class="wrap">
  <header>
    <h1>Mini <em>Football</em> — app icon</h1>
    <p>${sub}</p>
  </header>
  <div class="grid">${cards}</div>
  <div class="strips">
    ${strip(P.bgDeep, 'Dark launcher')}
    ${strip('#EDEFF3', 'Light launcher')}
  </div>
</div>`;

  const dest = join(OUT, `${name}.html`);
  writeFileSync(dest, html);
  shoot(html, 1240, h, join(OUT, `${name}.png`));
  items.forEach(c => writeFileSync(join(OUT, `${c.id}.svg`), icon(c, { size: 512 })));
  console.log(`sheet -> ${dest}`);
}

// ------------------------------------------------------------------- ship one
const SHIP = [
  // Unity: Player Settings > Android > Icon. Adaptive layers are 432, the rest 512.
  { file: 'Icon_Adaptive_Foreground.png', size: 432, layer: 'fg-adaptive', alpha: true },
  { file: 'Icon_Adaptive_Background.png', size: 432, layer: 'bg-adaptive', alpha: false },
  { file: 'Icon_Round.png', size: 512, layer: 'full', mask: 'circle', alpha: true },
  { file: 'Icon_Legacy.png', size: 512, layer: 'full', mask: 'squircle', alpha: true },
];

function ship(id) {
  const c = byId(id);
  if (!c) throw new Error(`unknown concept "${id}" (have: ${ALL.map(x => x.id).join(', ')})`);

  const unity = join(HERE, '..', '..', 'Assets', 'Images', 'Icon');
  const store = join(OUT, 'store');
  // Editable source stays out of Assets/ -- Unity has no SVG importer here and
  // would carry each one as an opaque DefaultAsset plus a .meta file.
  const src = join(OUT, 'ship');
  mkdirSync(unity, { recursive: true });
  mkdirSync(store, { recursive: true });
  mkdirSync(src, { recursive: true });

  for (const s of SHIP) {
    const svg = icon(c, { size: s.size, layer: s.layer, mask: s.mask });
    writeFileSync(join(src, s.file.replace('.png', '.svg')), svg);
    shoot(page(svg), s.size, s.size, join(unity, s.file), { transparent: s.alpha });
    console.log(`  ${s.file}  ${s.size}x${s.size}`);
  }

  // Play Store listing icon: 512, square, no transparency.
  shoot(page(icon(c, { size: 512, mask: 'square' })), 512, 512,
        join(store, 'PlayStore_512.png'));
  console.log('  store/PlayStore_512.png  512x512');

  // Play Store feature graphic: the mark on the arcade ground, wordmark alongside.
  // Play crops the outer edges of a feature graphic on some surfaces, so the
  // lockup is centred as a group rather than pinned to the left margin.
  const fg = `<div style="width:100vw;height:100vh;display:flex;align-items:center;
      justify-content:center;gap:52px;overflow:hidden;
      background:radial-gradient(80% 150% at 50% 38%, ${P.bgRaised} 0%, ${P.bgPanel} 45%, ${P.bgDeep} 100%)">
    <div style="width:312px;height:312px;flex:0 0 auto">${icon(c, { size: 312, mask: 'squircle' })}</div>
    <div style="font-family:Oswald,system-ui,sans-serif;letter-spacing:.1em">
      <div style="font-size:44px;font-weight:500;color:${P.ink};line-height:1">MINI</div>
      <div style="font-size:96px;font-weight:600;color:${P.gold};line-height:1.02">FOOTBALL</div>
      <div style="font-family:Inter,system-ui,sans-serif;font-size:19px;letter-spacing:.02em;
                  color:${P.inkMuted};margin-top:14px">${TAGLINE}</div>
    </div>
  </div>`;
  shoot(`<link href="https://fonts.googleapis.com/css2?family=Oswald:wght@500;600&family=Inter:wght@400&display=swap" rel="stylesheet">${fg}`,
        1024, 500, join(store, 'FeatureGraphic_1024x500.png'));
  console.log('  store/FeatureGraphic_1024x500.png  1024x500');

  console.log(`\nshipped "${c.name}" -> Assets/Images/Icon/ and Art/Icon/out/store/`);
}

// ------------------------------------------------------------------ palette
// render.py needs these colours and cannot import an .mjs module. Dumping them
// keeps lib/palette.mjs the one place a colour is written down; the renderer
// reads the dump rather than carrying its own copy that could quietly drift.
function palette() {
  mkdirSync(OUT, { recursive: true });
  const dest = join(OUT, 'palette.json');
  writeFileSync(dest, JSON.stringify({ P, D }, null, 2) + '\n');
  console.log(`  out/palette.json  ${Object.keys(P).length + Object.keys(D).length} colours`);
}

const [cmd, arg] = process.argv.slice(2);
if (cmd === 'ship') ship(arg || 'render3d');
else if (cmd === 'palette') palette();
else if (cmd === 'glyph')
  sheet(GLYPHS, 'glyph-sheet',
        'The Glyph — one flat figure, two colours. Three grounds, so the polarity gets chosen off a render rather than a guess.',
        1240);
else sheet();
