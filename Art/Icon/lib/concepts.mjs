import { P, D } from './palette.mjs';
import { render3d } from './render3d.mjs';

// ---------------------------------------------------------------------------
// Every mark is authored in a 1000x1000 space and must stay inside r=480 of
// (500,500) -- the only region Android guarantees an adaptive icon will show.
// Backgrounds are full-bleed 1000x1000 and are expected to be cropped.
// ---------------------------------------------------------------------------

/** Rounded-polyline limb. Round joins do the modelling, so the shape stays one path. */
const limb = (pts, color, w) =>
  `<polyline points="${pts.map(p => p.join(',')).join(' ')}" fill="none" stroke="${color}" ` +
  `stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`;

/** Solid shape whose corners are rounded by stroking it in its own fill colour. */
const blob = (pts, color, r) =>
  `<polygon points="${pts.map(p => p.join(',')).join(' ')}" fill="${color}" stroke="${color}" ` +
  `stroke-width="${r}" stroke-linejoin="round"/>`;

/** The chrome rod: bright core, shaded underside, dark grips at both ends. */
function rod(u, x1, x2, y, h, { grips = true } = {}) {
  const r = h / 2, g = h * 3.4;
  return `
    <rect x="${x1}" y="${y - r}" width="${x2 - x1}" height="${h}" rx="${r}" fill="url(#${u}chrome)"/>
    ${grips ? `
    <rect x="${x1}" y="${y - r}" width="${g}" height="${h}" rx="${r}" fill="${D.grip}"/>
    <rect x="${x2 - g}" y="${y - r}" width="${g}" height="${h}" rx="${r}" fill="${D.grip}"/>` : ''}
    <rect x="${x1 + h * 0.5}" y="${y - r * 0.62}" width="${x2 - x1 - h}" height="${h * 0.18}"
          rx="${h * 0.09}" fill="${D.chrome}" opacity=".7"/>`;
}

const chromeDef = u => `
  <linearGradient id="${u}chrome" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="${D.chrome}"/>
    <stop offset=".45" stop-color="${D.chromeMid}"/>
    <stop offset="1" stop-color="${D.chromeLow}"/>
  </linearGradient>`;

/** The shared ground: raised centre falling to deep edge, with team-coloured bloom. */
const arcadeGround = (u, { redX = 130, blueX = 870, bloom = 0.5 } = {}) => ({
  defs: `
    <radialGradient id="${u}ground" cx=".5" cy=".42" r=".78">
      <stop offset="0" stop-color="${P.bgRaised}"/>
      <stop offset=".55" stop-color="${P.bgPanel}"/>
      <stop offset="1" stop-color="${P.bgDeep}"/>
    </radialGradient>
    <radialGradient id="${u}rb" cx=".5" cy=".5" r=".5">
      <stop offset="0" stop-color="${P.red}" stop-opacity="${bloom}"/>
      <stop offset="1" stop-color="${P.red}" stop-opacity="0"/>
    </radialGradient>
    <radialGradient id="${u}bb" cx=".5" cy=".5" r=".5">
      <stop offset="0" stop-color="${P.blue}" stop-opacity="${bloom}"/>
      <stop offset="1" stop-color="${P.blue}" stop-opacity="0"/>
    </radialGradient>`,
  body: `
    <rect width="1000" height="1000" fill="url(#${u}ground)"/>
    <circle cx="${redX}" cy="620" r="440" fill="url(#${u}rb)"/>
    <circle cx="${blueX}" cy="620" r="440" fill="url(#${u}bb)"/>`,
});

/** A ball, lit from the upper left. */
const ball = (u, cx, cy, r, tint = P.ink) => `
  <circle cx="${cx}" cy="${cy}" r="${r}" fill="url(#${u}ball)"/>
  <circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="${tint}" stroke-opacity=".35"
          stroke-width="${r * 0.06}"/>
  <ellipse cx="${cx - r * 0.3}" cy="${cy - r * 0.36}" rx="${r * 0.38}" ry="${r * 0.28}"
           fill="#fff" opacity=".55" transform="rotate(-28 ${cx - r * 0.3} ${cy - r * 0.36})"/>`;

const ballDef = u => `
  <radialGradient id="${u}ball" cx=".36" cy=".3" r=".85">
    <stop offset="0" stop-color="#FFFFFF"/>
    <stop offset=".55" stop-color="${P.ink}"/>
    <stop offset="1" stop-color="#9AA8BB"/>
  </radialGradient>`;

// ===========================================================================
// 1. THE KEEPER -- one figure, front on, hanging off its rod, boot on the ball.
// ===========================================================================
const keeper = {
  id: 'keeper',
  name: 'The Keeper',
  line: 'One figure, front on, boot on the ball.',
  defs: u => `
    ${chromeDef(u)}${ballDef(u)}
    <linearGradient id="${u}fig" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="${D.goldLight}"/>
      <stop offset=".45" stop-color="${P.gold}"/>
      <stop offset="1" stop-color="${D.goldDeep}"/>
    </linearGradient>
    <radialGradient id="${u}halo" cx=".5" cy=".5" r=".5">
      <stop offset="0" stop-color="${P.gold}" stop-opacity=".38"/>
      <stop offset="1" stop-color="${P.gold}" stop-opacity="0"/>
    </radialGradient>`,
  bgDefs: u => arcadeGround(u).defs,
  bg: u => arcadeGround(u).body,
  fg: u => `
    <circle cx="500" cy="470" r="430" fill="url(#${u}halo)"/>
    <ellipse cx="510" cy="855" rx="250" ry="42" fill="${P.bgDeep}" opacity=".55"/>
    ${rod(u, 100, 900, 292, 46)}
    ${limb([[545, 560], [625, 700], [706, 748]], `url(#${u}fig)`, 74)}
    ${limb([[458, 560], [452, 786]], `url(#${u}fig)`, 78)}
    <rect x="404" y="754" width="104" height="52" rx="22" fill="${D.goldDeep}"/>
    ${blob([[404, 300], [596, 300], [630, 470], [578, 566], [422, 566], [370, 470]], `url(#${u}fig)`, 34)}
    <circle cx="500" cy="196" r="88" fill="url(#${u}fig)"/>
    <circle cx="474" cy="172" r="30" fill="${D.goldLight}" opacity=".55"/>
    ${ball(u, 772, 774, 74)}`,
};

// ===========================================================================
// 2. CROSS BARS -- the in-app logo mark promoted to an emblem.
// ===========================================================================
const crossbars = {
  id: 'crossbars',
  name: 'Cross Bars',
  line: 'The in-app logo mark, promoted to an emblem.',
  defs: u => `
    ${ballDef(u)}
    <linearGradient id="${u}r" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#FF6B84"/><stop offset=".5" stop-color="${P.red}"/>
      <stop offset="1" stop-color="${D.redDeep}"/>
    </linearGradient>
    <linearGradient id="${u}b" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#6FCBFF"/><stop offset=".5" stop-color="${P.blue}"/>
      <stop offset="1" stop-color="${D.blueDeep}"/>
    </linearGradient>
    <radialGradient id="${u}halo" cx=".5" cy=".5" r=".5">
      <stop offset="0" stop-color="${P.gold}" stop-opacity=".5"/>
      <stop offset=".55" stop-color="${P.gold}" stop-opacity=".12"/>
      <stop offset="1" stop-color="${P.gold}" stop-opacity="0"/>
    </radialGradient>`,
  bgDefs: u => arcadeGround(u, { bloom: 0.34 }).defs,
  bg: u => arcadeGround(u, { bloom: 0.34 }).body,
  fg: u => {
    const bar = (grad, rot) => `
      <g transform="rotate(${rot} 500 500)">
        <rect x="70" y="463" width="860" height="74" rx="37" fill="url(#${grad})"/>
        <rect x="70" y="463" width="118" height="74" rx="37" fill="${D.grip}"/>
        <rect x="812" y="463" width="118" height="74" rx="37" fill="${D.grip}"/>
        <rect x="200" y="479" width="596" height="13" rx="6" fill="#fff" opacity=".3"/>
      </g>`;
    return `
      <circle cx="500" cy="500" r="470" fill="url(#${u}halo)"/>
      <circle cx="500" cy="500" r="404" fill="none" stroke="${P.gold}" stroke-width="20"
              stroke-opacity=".85" stroke-dasharray="1000 130 480 130" stroke-linecap="round"
              transform="rotate(-31 500 500)"/>
      ${bar(u + 'b', 31)}
      ${bar(u + 'r', -31)}
      ${ball(u, 500, 500, 152)}`;
  },
};

// ===========================================================================
// 3. KICKOFF -- the game's own top-down camera, cropped to the centre spot.
// ===========================================================================
const kickoff = {
  id: 'kickoff',
  name: 'Kickoff',
  line: "The game's own camera, cropped to the centre spot.",
  defs: u => `
    ${chromeDef(u)}${ballDef(u)}
    <linearGradient id="${u}turf" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="${D.pitchLit}"/>
      <stop offset="1" stop-color="${D.pitch}"/>
    </linearGradient>
    <radialGradient id="${u}vig" cx=".5" cy=".45" r=".72">
      <stop offset=".45" stop-color="#000" stop-opacity="0"/>
      <stop offset="1" stop-color="#000" stop-opacity=".5"/>
    </radialGradient>`,
  bgDefs: u => arcadeGround(u, { bloom: 0.3 }).defs,
  bg: u => arcadeGround(u, { bloom: 0.3 }).body,
  fg: u => {
    const man = (x, y, c, cd) => `
      <ellipse cx="${x + 12}" cy="${y + 16}" rx="46" ry="34" fill="#000" opacity=".38"/>
      <rect x="${x - 40}" y="${y - 56}" width="80" height="112" rx="30" fill="${cd}"/>
      <rect x="${x - 40}" y="${y - 56}" width="80" height="86" rx="30" fill="${c}"/>
      <circle cx="${x}" cy="${y - 14}" r="27" fill="${D.skin}"/>`;
    const ln = (d, w = 11) =>
      `<path d="${d}" fill="none" stroke="${P.ink}" stroke-opacity=".8" stroke-width="${w}"/>`;
    return `
      <rect x="196" y="136" width="608" height="728" rx="52" fill="url(#${u}turf)"/>
      <rect x="196" y="136" width="608" height="728" rx="52" fill="url(#${u}vig)"/>
      ${ln('M236 176 H764 V824 H236 Z')}
      ${ln('M236 500 H764')}
      ${ln('M500 384 a116 116 0 1 0 0.1 0')}
      ${ln('M368 176 H632 V286 H368 Z')}
      ${ln('M368 824 H632 V714 H368 Z')}
      <rect x="196" y="136" width="608" height="728" rx="52" fill="none"
            stroke="${P.line}" stroke-width="16"/>
      <rect x="424" y="122" width="152" height="26" rx="10" fill="${P.gold}"/>
      <rect x="424" y="852" width="152" height="26" rx="10" fill="${P.gold}"/>
      ${rod(u, 132, 868, 332, 30, { grips: false })}
      ${man(392, 332, P.red, D.redDeep)}${man(608, 332, P.red, D.redDeep)}
      ${rod(u, 132, 868, 668, 30, { grips: false })}
      ${man(392, 668, P.blue, D.blueDeep)}${man(608, 668, P.blue, D.blueDeep)}
      ${ball(u, 500, 500, 54)}`;
  },
};

// ===========================================================================
// 4. THE STRIKE -- the moment of contact, everything else deleted.
// ===========================================================================
const strike = {
  id: 'strike',
  name: 'The Strike',
  line: 'The moment of contact, everything else deleted.',
  defs: u => `
    ${ballDef(u)}
    <linearGradient id="${u}trail" x1="0" y1="1" x2="1" y2="0">
      <stop offset="0" stop-color="${P.gold}" stop-opacity="0"/>
      <stop offset=".45" stop-color="${P.gold}" stop-opacity=".55"/>
      <stop offset="1" stop-color="${D.goldLight}" stop-opacity=".95"/>
    </linearGradient>
    <linearGradient id="${u}boot" x1="0" y1="1" x2="1" y2="0">
      <stop offset="0" stop-color="${D.redDeep}"/><stop offset="1" stop-color="#FF6B84"/>
    </linearGradient>
    <radialGradient id="${u}flash" cx=".5" cy=".5" r=".5">
      <stop offset="0" stop-color="${D.goldLight}" stop-opacity=".85"/>
      <stop offset=".4" stop-color="${P.gold}" stop-opacity=".3"/>
      <stop offset="1" stop-color="${P.gold}" stop-opacity="0"/>
    </radialGradient>`,
  bgDefs: u => arcadeGround(u, { redX: 240, blueX: 900, bloom: 0.42 }).defs,
  bg: u => arcadeGround(u, { redX: 240, blueX: 900, bloom: 0.42 }).body,
  fg: u => `
    <circle cx="646" cy="372" r="420" fill="url(#${u}flash)"/>
    <path d="M556 254 Q300 470 176 826 Q436 668 736 466 Z" fill="url(#${u}trail)"/>
    <path d="M470 300 Q286 452 196 700" fill="none" stroke="${P.gold}" stroke-opacity=".5"
          stroke-width="16" stroke-linecap="round"/>
    <path d="M690 520 Q520 660 300 800" fill="none" stroke="${P.gold}" stroke-opacity=".38"
          stroke-width="12" stroke-linecap="round"/>
    ${limb([[228, 828], [352, 760], [452, 706]], `url(#${u}boot)`, 86)}
    <circle cx="646" cy="372" r="152" fill="${P.gold}" opacity=".22"/>
    ${ball(u, 646, 372, 138)}
    <g stroke="${D.goldLight}" stroke-width="15" stroke-linecap="round" opacity=".9">
      <path d="M520 250 L470 196"/><path d="M604 200 L586 132"/><path d="M700 190 L724 126"/>
      <path d="M792 258 L850 216"/>
    </g>`,
};

// ===========================================================================
// 5. NET BUSTER -- the payoff: the ball already past the line.
// ===========================================================================
const netbuster = {
  id: 'netbuster',
  name: 'Net Buster',
  line: 'The payoff -- the ball already past the line.',
  defs: u => `
    ${ballDef(u)}
    <linearGradient id="${u}post" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0" stop-color="${D.goldDeep}"/><stop offset=".4" stop-color="${D.goldLight}"/>
      <stop offset="1" stop-color="${P.gold}"/>
    </linearGradient>
    <radialGradient id="${u}burst" cx=".5" cy=".5" r=".5">
      <stop offset="0" stop-color="#fff" stop-opacity=".75"/>
      <stop offset=".35" stop-color="${P.gold}" stop-opacity=".35"/>
      <stop offset="1" stop-color="${P.gold}" stop-opacity="0"/>
    </radialGradient>`,
  bgDefs: u => arcadeGround(u, { redX: 90, blueX: 910, bloom: 0.44 }).defs,
  bg: u => arcadeGround(u, { redX: 90, blueX: 910, bloom: 0.44 }).body,
  fg: u => {
    // The net bulges around the ball: every strand's control point is pushed
    // radially away from the impact, hardest where it passes closest.
    const BX = 500, BY = 512, BR = 158, PUSH = 210;
    const push = (x, y) => {
      const dx = x - BX, dy = y - BY, d = Math.hypot(dx, dy) || 1;
      const k = Math.max(0, 1 - d / (BR * 3.1));
      const f = (PUSH * k * k) / d;
      return [x + dx * f, y + dy * f];
    };
    let net = '';
    for (let i = 0; i <= 12; i++) {
      const x = 196 + i * (608 / 12);
      const [cx, cy] = push(x, 512);
      net += `<path d="M${x.toFixed(1)} 268 Q${cx.toFixed(1)} ${cy.toFixed(1)} ${x.toFixed(1)} 826"/>`;
    }
    for (let j = 0; j <= 10; j++) {
      const y = 268 + j * (558 / 10);
      const [cx, cy] = push(500, y);
      net += `<path d="M196 ${y.toFixed(1)} Q${cx.toFixed(1)} ${cy.toFixed(1)} 804 ${y.toFixed(1)}"/>`;
    }
    return `
      <rect x="196" y="268" width="608" height="558" rx="18" fill="${P.bgDeep}" opacity=".72"/>
      <g fill="none" stroke="${P.inkMuted}" stroke-opacity=".55" stroke-width="7">${net}</g>
      <circle cx="${BX}" cy="${BY}" r="430" fill="url(#${u}burst)"/>
      <g stroke="url(#${u}post)" stroke-width="38" stroke-linecap="round" fill="none">
        <path d="M196 262 H804"/><path d="M196 268 V830"/><path d="M804 268 V830"/>
      </g>
      ${ball(u, BX, BY, BR, P.gold)}`;
  },
};

// ===========================================================================
// THE GLYPH -- the chosen direction. One flat figure, two colours, nothing else.
//
// No rod, no ball, no shading. What has to survive 28px is the silhouette, so
// every limb shares one thickness, the head keeps a clear gap from the
// shoulders, and the legs diverge downward to hold their slot open.
//
// The whole figure is nudged 38 units left: the kicking foot reaches right, and
// centring the bounding box would leave the body itself sitting off-axis.
// ===========================================================================
const NUDGE = -20;

/**
 * The rod is the whole argument: a foosball man without one is a pedestrian sign.
 * It is drawn as an unbroken band with squared ends, running clear across the
 * tile -- head resting on top of it, body hanging below. Rounded ends at shoulder
 * height read as outstretched arms instead, which is the trap this shape avoids.
 */
const glyphFigure = (fill, rodFill = fill) => `
  <g transform="translate(${NUDGE} 0)">
    <circle cx="500" cy="202" r="90" fill="${fill}"/>
    ${blob([[370, 322], [630, 322], [596, 604], [404, 604]], fill, 46)}
    ${limb([[444, 592], [436, 844]], fill, 78)}
    ${limb([[566, 592], [708, 786]], fill, 78)}
    <rect x="152" y="338" width="696" height="54" rx="12" fill="${rodFill}"/>
  </g>`;

const glyphVariant = (id, name, line, figure, ground, rod) => ({
  id, name, line,
  defs: () => '',
  bgDefs: () => '',
  bg: () => `<rect width="1000" height="1000" fill="${ground}"/>`,
  fg: () => glyphFigure(figure, rod),
});

// The ground is BgRaised, not BgDeep. BgDeep is so close to a black wallpaper
// that the tile loses its edge entirely and the figure reads as floating -- the
// proof pass showed it. BgRaised is the same world, one step up.
export const GLYPHS = [
  glyphVariant('glyph', 'One colour',
               'Gold figure, gold rod. Two colours on the tile, exactly as briefed.',
               P.gold, P.bgRaised),
  glyphVariant('glyph-rod', 'Chrome rod',
               'Rod in Ink so it reads as a rod and not as arms. Costs a third flat colour.',
               P.gold, P.bgRaised, P.ink),
  glyphVariant('glyph-inverse', 'Inverted',
               "The theme's own OnGold pairing. Loudest of the three.",
               P.onGold, P.gold),
];

export const CONCEPTS = [keeper, crossbars, kickoff, strike, netbuster];
export const ALL = [render3d, ...GLYPHS, ...CONCEPTS];
export const byId = id => ALL.find(c => c.id === id);

/**
 * Assemble one concept into a standalone SVG document.
 * layer: 'full' (legacy / store / preview) | 'fg-adaptive' | 'bg-adaptive'
 * Adaptive layers are full-bleed and are masked by the launcher, never by us.
 */
export function icon(c, { size = 512, layer = 'full', mask = 'squircle' } = {}) {
  const u = `${c.id}-${layer}-`;
  const clipped = layer === 'full';
  const clip =
    mask === 'circle' ? `<clipPath id="${u}m"><circle cx="500" cy="500" r="500"/></clipPath>` :
    mask === 'square' ? `<clipPath id="${u}m"><rect width="1000" height="1000"/></clipPath>` :
    `<clipPath id="${u}m"><rect width="1000" height="1000" rx="224"/></clipPath>`;

  const wantsBg = layer === 'full' || layer === 'bg-adaptive';
  const wantsFg = layer === 'full' || layer === 'fg-adaptive';

  // 'full' fills 78% of the tile. An adaptive foreground must land inside the
  // 66dp safe circle of a 108dp layer, i.e. 52.8% of the layer.
  // A rendered mark is already framed against its own background by the camera;
  // rescaling its foreground alone would slide the figure off its own shadow.
  const s = c.prescaled ? 1 : (layer === 'fg-adaptive' ? 0.528 : 0.78);
  const off = (1000 - 1000 * s) / 2;

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 1000 1000">
<defs>${clipped ? clip : ''}${wantsBg ? c.bgDefs(u, layer) : ''}${wantsFg ? c.defs(u, layer) : ''}</defs>
<g${clipped ? ` clip-path="url(#${u}m)"` : ''}>
${wantsBg ? c.bg(u, layer) : ''}
${wantsFg ? `<g transform="translate(${off.toFixed(2)} ${off.toFixed(2)}) scale(${s})">${c.fg(u, layer)}</g>` : ''}
</g></svg>`;
}
