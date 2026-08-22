// The 3D mark, wrapped so the rest of the pipeline cannot tell it apart from a
// drawn one. render.py produces three frames; this presents them as a concept
// with the same {defs, bg, fg} shape as the SVG marks, which is what lets
// build.mjs and verify.mjs stay untouched.
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const RENDER = join(dirname(fileURLToPath(import.meta.url)), '..', 'out', 'render');

/** A rendered frame as a data URI, so the exported SVG stays a single file. */
function frame(name) {
  const path = join(RENDER, name);
  if (!existsSync(path))
    throw new Error(
      `missing ${name} -- run:\n` +
      '  node build.mjs palette\n' +
      '  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup -P render.py');
  return `data:image/png;base64,${readFileSync(path).toString('base64')}`;
}

// slice, not meet: the frames are square and the authoring space is square, so
// this is a no-op today. It is here so a non-square frame would crop rather than
// letterbox -- a letterboxed adaptive layer would show bars through the mask.
const full = href =>
  `<image href="${href}" x="0" y="0" width="1000" height="1000" preserveAspectRatio="xMidYMid slice"/>`;

export const render3d = {
  id: 'render3d',
  name: 'The Figure',
  line: 'One red man on his rod, three-quarter view, full-bleed pitch. Rendered, not drawn.',

  // The renders are already framed against each other by the camera. icon()'s
  // usual trick of scaling the foreground into the safe circle would slide the
  // figure off its own shadow, so this concept opts out of that scaling and
  // takes responsibility for the safe zone itself, in render.py's FRAME_WIDE.
  prescaled: true,

  defs: () => '',
  bgDefs: () => '',

  // 'full' is the legacy / round / store tile: one already-composited frame, shot
  // closer because those tiles are never cropped to the adaptive safe circle.
  bg: (u, layer) => full(frame(layer === 'bg-adaptive' ? 'field_wide.png' : 'composite_tight.png')),

  // Only the adaptive foreground carries a separate layer. For 'full' the figure
  // is already in the background frame, and drawing it twice would double the
  // contact shadow.
  fg: (u, layer) => (layer === 'fg-adaptive' ? full(frame('figure_wide.png')) : ''),
};
