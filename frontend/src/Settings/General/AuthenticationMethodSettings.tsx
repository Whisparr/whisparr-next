import React from 'react';
import FormsAuthenticationSettings from './FormsAuthenticationSettings';
import { GeneralSettingsSectionProps } from './GeneralSettingsProps';
import OidcAuthenticationSettings from './OidcAuthenticationSettings';

interface AuthenticationMethodSettingsProps extends GeneralSettingsSectionProps {
  showValidationWarnings?: boolean;
}

function AuthenticationMethodSettings({
  settings,
  showValidationWarnings,
  onInputChange,
}: Readonly<AuthenticationMethodSettingsProps>) {
  switch (settings.authenticationMethod?.value) {
    case 'forms':
      return (
        <FormsAuthenticationSettings
          settings={settings}
          showValidationWarnings={showValidationWarnings}
          onInputChange={onInputChange}
        />
      );

    case 'oidc':
      return (
        <OidcAuthenticationSettings
          settings={settings}
          showValidationWarnings={showValidationWarnings}
          onInputChange={onInputChange}
        />
      );

    default:
      return null;
  }
}

export default AuthenticationMethodSettings;
