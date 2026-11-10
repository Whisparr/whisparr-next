import { useNavigate } from 'react-router-dom';
import useApiMutation from 'Helpers/Hooks/useApiMutation';

interface DeleteMoviesPayload {
  movieIds: number[];
  deleteFiles: boolean;
  addImportExclusion: boolean;
}

export function useDeleteMoviesMutation() {
  const navigate = useNavigate();

  const mutation = useApiMutation<unknown, DeleteMoviesPayload>({
    method: 'DELETE',
    path: '/movie/editor',
    mutationOptions: {
      onSuccess: () => {
        navigate('/movies');
      },
    },
  });

  return mutation;
}
