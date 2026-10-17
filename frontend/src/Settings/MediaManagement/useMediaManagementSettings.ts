import { useManageSettings, useSettings } from 'Settings/useSettings';
import MediaManagement from 'typings/Settings/MediaManagement';

// Sonarr's copy reads `/settings/mediamanagement`; that route is API v5, and
// so is this one: Whisparr v5 serves the config routes under /config.
const PATH = '/config/mediamanagement';

export const useMediaManagementSettings = () => {
  return useSettings<MediaManagement>(PATH);
};

export const useManageMediaManagementSettings = () => {
  return useManageSettings<MediaManagement>(PATH);
};
