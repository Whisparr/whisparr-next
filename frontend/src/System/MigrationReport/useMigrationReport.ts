import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import MigrationReport from 'typings/MigrationReport';

const PATH = '/system/migrationreport';

// The server answers 404 when no migration ever ran. That is an expected
// state, not a failure, so it is not retried; callers read `hasReport`.
const useMigrationReport = () => {
  const result = useApiQuery<MigrationReport>({
    path: PATH,
    queryOptions: {
      retry: false,
    },
  });

  return {
    ...result,
    hasReport: !!result.data && !result.isError,
  };
};

export default useMigrationReport;

export const useDismissMigrationReport = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<object, void>({
    path: `${PATH}/dismiss`,
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [PATH] });
        queryClient.invalidateQueries({ queryKey: ['/health'] });
      },
    },
  });

  return {
    dismissReport: mutate,
    isDismissing: isPending,
    dismissError: error,
  };
};
