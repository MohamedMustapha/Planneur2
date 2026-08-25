import { readFileSync } from 'node:fs';

/**
 * The accessibility half of §02.4, as a test rather than as a promise.
 *
 * Two claims are checked, and neither survives review alone. Contrast, because a token file is a list of hex
 * codes and nobody eyeballs 4.5:1; and the density contract, because Focus mode tightens the rhythm and the
 * obvious way to do that — shrink the controls — reproduces exactly the 2010 admin console this theme replaced.
 */
// Relative to the workspace root, which is where the runner starts. `import.meta.url` is not a file URL once
// the spec has been bundled, so it cannot be resolved from here.
const TOKENS = readFileSync('src/styles/_tokens.scss', 'utf8');

type Rgb = readonly [number, number, number];

function block(selector: string): string {
  const start = TOKENS.indexOf(selector);

  expect(start).toBeGreaterThan(-1);

  const open = TOKENS.indexOf('{', start);
  const close = TOKENS.indexOf('\n}', open);

  return TOKENS.slice(open, close);
}

function tokens(selector: string): Record<string, string> {
  const found: Record<string, string> = {};

  for (const [, name, value] of block(selector).matchAll(/(--[\w-]+):\s*([^;]+);/g)) {
    found[name] = value.trim();
  }

  return found;
}

const light = tokens(':root {');
const dark = tokens(":root[data-theme='dark']");

function resolve(name: string, theme: Record<string, string>): string {
  const value = theme[name] ?? light[name];

  expect(value, `${name} is defined`).toBeDefined();

  const alias = /^var\((--[\w-]+)\)$/.exec(value);

  return alias ? resolve(alias[1], theme) : value;
}

function rgb(hex: string): Rgb {
  const digits = hex.replace('#', '').slice(0, 6);
  const wide = digits.length === 3 ? [...digits].map((digit) => digit + digit).join('') : digits;

  return [
    parseInt(wide.slice(0, 2), 16),
    parseInt(wide.slice(2, 4), 16),
    parseInt(wide.slice(4, 6), 16),
  ];
}

/** WCAG 2.1 relative luminance. */
function luminance(colour: Rgb): number {
  const [r, g, b] = colour.map((channel) => {
    const ratio = channel / 255;

    return ratio <= 0.03928 ? ratio / 12.92 : ((ratio + 0.055) / 1.055) ** 2.4;
  });

  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(foreground: string, background: string): number {
  const [light_, dark_] = [luminance(rgb(foreground)), luminance(rgb(background))].sort(
    (a, b) => b - a,
  );

  return (light_ + 0.05) / (dark_ + 0.05);
}

function pixels(value: string): number {
  return Number.parseInt(value.replace('px', ''), 10);
}

/** Text token against the surface it is drawn on, in both themes. §02.4 wants 4.5:1 for all of them. */
const TEXT_ON_SURFACE: readonly (readonly [string, string])[] = [
  ['--ink', '--surface'],
  ['--ink-2', '--surface'],
  ['--ink', '--surface-2'],
  ['--ink-2', '--surface-2'],
  ['--ink', '--bg'],
  ['--ink-2', '--bg'],
  ['--primary-ink', '--primary-soft'],
  ['--rail-ink', '--rail-bg'],
  ['--rail-ink-muted', '--rail-bg'],
];

describe('theme tokens', () => {
  describe.each([
    ['light', light],
    ['dark', dark],
  ])('%s', (_name, theme) => {
    it.each(TEXT_ON_SURFACE)('%s reads on %s', (ink, surface) => {
      expect(contrast(resolve(ink, theme), resolve(surface, theme))).toBeGreaterThanOrEqual(4.5);
    });

    it('draws white on the primary action', () => {
      expect(contrast('#ffffff', resolve('--primary', theme))).toBeGreaterThanOrEqual(4.5);
    });
  });

  describe('density contract', () => {
    it('sizes controls for a finger rather than a mouse', () => {
      // Fitts's law is not a style preference. 36px survives as the dense-grid exception and nothing smaller is
      // allowed to be the default.
      expect(pixels(light['--control-height'])).toBe(44);
      expect(pixels(light['--control-height-sm'])).toBeGreaterThanOrEqual(36);
    });

    it('sets the base type at the size §4 calls the biggest anti-2010 lever', () => {
      expect(pixels(light['--text-base'])).toBe(16);
      expect(pixels(light['--text-sm'])).toBe(14);
    });

    it('tightens rows in Focus mode without shrinking a control', () => {
      const focus = tokens(":root[data-focus='on']");

      expect(pixels(light['--row-h'])).toBe(56);
      expect(pixels(focus['--row-h'])).toBeLessThan(pixels(light['--row-h']));

      // "Focus is about fewer things, not smaller things." Getting this backwards reproduces the problem the
      // whole slice exists to fix, and it is a one-line edit away at all times.
      expect(pixels(focus['--row-h'])).toBeGreaterThanOrEqual(40);
      expect(focus['--control-height']).toBeUndefined();
    });
  });
});
