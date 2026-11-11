import light from './light';

const whisparrPink = '#ff69b4';
const alternatePink = '#c4337c';

// V2's light palette: V5's light with V2's values where the two differ, and the
// purple accents V5 added since pointed at V2's pinks.
// Only keys the base already has: ApplyTheme never clears a variable, so an
// extra one would stay set after switching back to a standard theme.
const pinkLight = {
  ...light,
  infoColor: whisparrPink,
  themeDarkColor: '#3a3f51',
  themeLightColor: '#4f566f',
  pageHeaderBackgroundColor: whisparrPink,
  sidebarBackgroundColor: '#3a3f51',
  sidebarActiveBackgroundColor: '#252833',
  toolbarBackgroundColor: '#4f566f',
  toolbarMenuItemBackgroundColor: '#454b60',
  toolbarMenuItemHoverBackgroundColor: '#3a3f51',
  toolbarLabelColor: '#8895aa',
  toobarButtonHoverColor: '#35c5f4',
  toobarButtonSelectedColor: '#35c5f4',
  popoverTitleBackgroundInverseColor: '#3a3f51',
  popoverTitleBorderInverseColor: '#4f566f',
  tableRowHoverBackgroundColor: '#c5c5c5',

  themePurple: alternatePink,
  whisparrPurple: whisparrPink,
  themeAlternatePurple: '#ff9ccd',
};

export default pinkLight;
