import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { useJavAvailable } from 'Settings/MetadataSource/useMetadataSources';

// Search results don't carry hasJav, so the add dialogs ask for it per item.
function useLookupHasJav(kind: 'studio' | 'performer', foreignId: string) {
  const javAvailable = useJavAvailable();

  const { data } = useApiQuery<{ hasJav: boolean }>({
    path: `/lookup/${kind}/${foreignId}/hasjav`,
    queryOptions: { enabled: javAvailable && !!foreignId },
  });

  return javAvailable && data?.hasJav === true;
}

export default useLookupHasJav;
