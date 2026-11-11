import dark from './dark';
import light from './light';
import pinkDark from './pinkDark';
import pinkLight from './pinkLight';

export type ThemeName = 'auto' | 'light' | 'dark' | 'pinkLight' | 'pinkDark';

// The pink themes bring V2's logos with them; the rest keep V5's.
export const isPinkTheme = (theme: string) => theme.startsWith('pink');

export const imagesPath = (theme: string) =>
  `${window.Whisparr.urlBase}/Content/Images${isPinkTheme(theme) ? '/V2' : ''}`;

// Every variable both palettes define. The two are not key-identical, so this
// is the intersection rather than either one on its own; the four keys that
// only one side has are listed below rather than hidden behind an index
// signature.
type SharedTheme = { [K in keyof typeof dark & keyof typeof light]: string };

// Three of these four are read from CSS by the palette that lacks them, so
// `var()` resolves to nothing in that theme, and the fourth is read by nothing
// at all. That is a palette bug rather than a conversion one -- it is recorded
// in REDUX_MIGRATION.md and fixed separately. When the palettes match, this
// block goes away and `Theme` becomes `SharedTheme`.
export type Theme = SharedTheme & {
  themeLightPurple?: string; // dark only; PageSidebarItem.css reads it
  sceneBackgroundColor?: string; // dark only; five poster stylesheets read it
  whisparrPurple?: string; // light only; four poster stylesheets read it
  themeAlternatePurple?: string; // light only; nothing reads it
};

const defaultDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
const auto = defaultDark ? dark : light;

// Exported anonymously so `filenames/match-exported` has no name to hold
// against the `Themes` directory.
export default {
  auto,
  light,
  dark,
  pinkLight,
  pinkDark,
} satisfies Record<ThemeName, Theme>;
