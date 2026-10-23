import ModelBase from 'App/ModelBase';
import Language from 'Language/Language';
import { QualityModel } from 'Quality/Quality';
import CustomFormat from 'typings/CustomFormat';
import MediaInfo from 'typings/MediaInfo';

// `MatchTier` (src/NzbDrone.Core/MediaFiles/MatchTier.cs), which the API
// serialises as a camelCase string (STJson's JsonStringEnumConverter).
export type MatchTier =
  'unknown' | 'name' | 'confirmed' | 'hash' | 'manual' | 'conflict';

export interface MovieFile extends ModelBase {
  movieId: number;
  relativePath: string;
  path: string;
  size: number;
  dateAdded: string;
  sceneName: string;
  edition: string;
  releaseGroup: string;
  languages: Language[];
  quality: QualityModel;
  customFormats: CustomFormat[];
  indexerFlags: number;
  mediaInfo: MediaInfo;
  qualityCutoffNotMet: boolean;
  matchTier?: MatchTier;
}
