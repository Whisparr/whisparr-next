// A scene the server suggests for a file from its fingerprint. Candidates come
// de-duplicated per scene and ordered oshash-first.
interface FingerprintCandidate {
  sceneId: string;
  algorithm: 'OSHASH' | 'PHASH';
  distance: number;
  submissions: number;
  duration: number;
  movieId: number | null;
  title?: string;
  studioTitle?: string;
  releaseDate?: string;
}

export default FingerprintCandidate;
