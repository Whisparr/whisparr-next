import React, { useCallback, useState } from 'react';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import { SelectStateInputProps } from 'typings/props';
import translate from 'Utilities/String/translate';
import UnmappedFileLinkModal from './UnmappedFileLinkModal';
import { UnmappedFile } from './useUnmappedFiles';
import styles from './UnmappedFileRow.module.css';

interface UnmappedFileRowProps {
  readonly file: UnmappedFile;
  readonly isSelected: boolean;
  readonly onSelectedChange: (options: SelectStateInputProps) => void;
  readonly onDismissPress: (id: number, dismissed: boolean) => void;
}

function UnmappedFileRow({
  file,
  isSelected,
  onSelectedChange,
  onDismissPress,
}: UnmappedFileRowProps) {
  const [isLinkModalOpen, setIsLinkModalOpen] = useState(false);

  const handleLinkPress = useCallback(() => {
    setIsLinkModalOpen(true);
  }, []);

  const handleLinkModalClose = useCallback(() => {
    setIsLinkModalOpen(false);
  }, []);

  const handleDismissPress = useCallback(() => {
    onDismissPress(file.id, !file.dismissed);
  }, [file.id, file.dismissed, onDismissPress]);

  return (
    <TableRow>
      <TableSelectCell
        id={file.id}
        isSelected={isSelected}
        onSelectedChange={onSelectedChange}
      />

      <TableRowCell className={styles.path}>
        {file.path}

        {file.exists ? null : (
          <Label className={styles.missing} kind={kinds.DANGER}>
            {translate('Missing')}
          </Label>
        )}
      </TableRowCell>

      <TableRowCell className={styles.actions}>
        <IconButton
          name={icons.SEARCH}
          title={translate('UnmappedFileLink')}
          isDisabled={!file.exists}
          onPress={handleLinkPress}
        />

        <IconButton
          name={file.dismissed ? icons.RESTORE : icons.HIDDEN}
          title={translate(file.dismissed ? 'Restore' : 'Dismiss')}
          onPress={handleDismissPress}
        />
      </TableRowCell>

      {isLinkModalOpen ? (
        <UnmappedFileLinkModal
          file={file}
          isOpen={isLinkModalOpen}
          onModalClose={handleLinkModalClose}
        />
      ) : null}
    </TableRow>
  );
}

export default UnmappedFileRow;
