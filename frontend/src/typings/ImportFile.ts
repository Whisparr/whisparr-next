import ModelBase from 'App/ModelBase';
import FingerprintCandidate from './FingerprintCandidate';

interface ImportFile extends ModelBase {
  id: number;
  rootFolderId: number;
  path: string;
  relativePath: string;
  name: string;
  foreignId: string;
  qualityProfileId: number;
  candidates?: FingerprintCandidate[] | null;
}

export default ImportFile;
