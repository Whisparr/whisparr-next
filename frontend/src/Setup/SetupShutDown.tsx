import React from 'react';
import Button from 'Components/Link/Button';
import translate from 'Utilities/String/translate';

interface SetupShutDownProps {
  isDocker: boolean;
  onPress: () => void;
}

// Exiting can't stop a container: its restart policy may just start it again.
function SetupShutDown({ isDocker, onPress }: Readonly<SetupShutDownProps>) {
  if (isDocker) {
    return <p>{translate('SetupShutDownDocker')}</p>;
  }

  return <Button onPress={onPress}>{translate('SetupShutDown')}</Button>;
}

export default SetupShutDown;
