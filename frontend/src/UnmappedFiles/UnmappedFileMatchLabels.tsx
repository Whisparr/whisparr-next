import React from 'react';
import Label from 'Components/Label';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { UnmappedFileMatchFlags } from './useUnmappedFiles';

interface UnmappedFileMatchLabelsProps {
  readonly match: UnmappedFileMatchFlags;
}

// Shared by the table's stored match and the link modal's results, so both read the same way
function UnmappedFileMatchLabels({ match }: UnmappedFileMatchLabelsProps) {
  return (
    <>
      {match.hashMatch ? (
        <Label kind={kinds.SUCCESS}>{translate('HashMatch')}</Label>
      ) : null}

      {match.codeMatch ? (
        <Label kind={kinds.SUCCESS}>{translate('CodeMatch')}</Label>
      ) : null}

      {match.sameTitle ? (
        <Label kind={kinds.INFO}>{translate('SameTitle')}</Label>
      ) : null}

      {match.sameDate ? (
        <Label kind={kinds.INFO}>{translate('SameDate')}</Label>
      ) : null}

      {/* A same date already says the year */}
      {match.sameYear && !match.sameDate ? (
        <Label kind={kinds.INFO}>{translate('SameYear')}</Label>
      ) : null}

      {match.sameStudio ? (
        <Label kind={kinds.INFO}>{translate('SameStudio')}</Label>
      ) : null}
    </>
  );
}

export default UnmappedFileMatchLabels;
