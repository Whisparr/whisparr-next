import type Movie from '../../Movie/Movie';

// The Works table is virtualized one row per work, not one per year, so only
// what is on screen is mounted however large the studio is. Each year becomes
// a header row and, when expanded, a column header row, a row per work and a
// footer row with the collapse button.
export type StudioWorksRow =
  | {
      type: 'year';
      key: string;
      year: number;
      works: Movie[];
      isExpanded: boolean;
    }
  | { type: 'columns'; key: string; year: number }
  | { type: 'work'; key: string; year: number; movie: Movie }
  | { type: 'footer'; key: string; year: number };

export interface StudioWorksYear {
  year: number;
  works: Movie[];
}

export function buildStudioWorksRows(
  worksByYear: readonly StudioWorksYear[],
  expandedState: Readonly<Record<number, boolean>>,
  sortWorks: (works: Movie[]) => Movie[]
): StudioWorksRow[] {
  const rows: StudioWorksRow[] = [];

  worksByYear.forEach(({ year, works }) => {
    if (!works.length) {
      return;
    }

    const isExpanded = !!expandedState[year];
    const sorted = sortWorks(works);

    rows.push({
      type: 'year',
      key: `year-${year}`,
      year,
      works: sorted,
      isExpanded,
    });

    if (!isExpanded) {
      return;
    }

    rows.push({ type: 'columns', key: `columns-${year}`, year });

    sorted.forEach((movie) => {
      // A work is listed under one year only, so its id is a stable key.
      rows.push({ type: 'work', key: `work-${movie.id}`, year, movie });
    });

    rows.push({ type: 'footer', key: `footer-${year}`, year });
  });

  return rows;
}
