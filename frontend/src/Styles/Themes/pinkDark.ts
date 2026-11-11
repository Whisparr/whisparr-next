import dark from './dark';

const whisparrPink = '#ff69b4';
const alternatePink = '#c4337c';

// V2's dark palette: V5's dark with V2's values where the two differ, and the
// purple accents V5 added since pointed at V2's pinks.
// Only keys the base already has: ApplyTheme never clears a variable, so an
// extra one would stay set after switching back to a standard theme.
const pinkDark = {
  ...dark,
  toolbarMenuItemBackgroundColor: '#333',
  toolbarMenuItemHoverBackgroundColor: '#414141',
  menuItemHoverColor: whisparrPink,
  toobarButtonHoverColor: whisparrPink,
  toobarButtonSelectedColor: whisparrPink,
  tableRowHoverBackgroundColor: '#3e3e3e',

  themePurple: alternatePink,
  themeLightPurple: whisparrPink,
};

export default pinkDark;
