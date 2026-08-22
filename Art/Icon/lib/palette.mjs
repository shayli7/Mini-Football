// Mirrors Assets/Scripts/UI/ArcadeTheme.cs. If a value changes there, change it here.
// The icon is the first frame of the game's UI, so it may not invent its own colours.
export const P = {
  bgDeep:   '#0A0E14',
  bgPanel:  '#141A24',
  bgRaised: '#1E2735',
  line:     '#2A3547',
  ink:      '#EAF0F7',
  inkMuted: '#8A97A8',
  red:      '#FF3355',
  blue:     '#22A7FF',
  gold:     '#FFB63D',
  go:       '#3DFF88',
  onGold:   '#241800',
};

// Derived tones the icon needs and the UI does not: shaded sides, chrome, pitch, wood.
export const D = {
  goldDeep:  '#C87F1E',
  goldLight: '#FFD98A',
  redDeep:   '#B31D38',
  blueDeep:  '#0F6FB5',
  chrome:    '#F2F6FB',
  chromeMid: '#B7C3D2',
  chromeLow: '#66748A',
  grip:      '#12161E',
  pitch:     '#123F27',
  pitchLit:  '#17512F',
  skin:      '#F0D3AC',
};

// Adaptive-icon geometry, in the 1000-unit space every mark below is drawn in.
// Android shows the centre 66.7% of an adaptive layer and guarantees only the
// inner circle. Everything in a mark must sit inside r=480 of (500,500).
export const SAFE_R = 480;
