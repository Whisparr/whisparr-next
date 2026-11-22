import assert from 'node:assert/strict';
import { test } from 'node:test';
import type Movie from '../../Movie/Movie';
import { buildStudioWorksRows } from './studioWorksRows.ts';

const work = (id: number, title: string) => ({ id, title }) as Movie;

const worksByYear = [
  { year: 2024, works: [work(1, 'B'), work(2, 'A')] },
  { year: 2023, works: [work(3, 'C')] },
  { year: 2022, works: [] },
];

const byTitle = (works: Movie[]) =>
  [...works].sort((a, b) => a.title.localeCompare(b.title));

test('a collapsed year is a single header row', () => {
  const rows = buildStudioWorksRows(worksByYear, {}, byTitle);

  assert.deepEqual(
    rows.map((row) => row.key),
    ['year-2024', 'year-2023']
  );
});

test('an expanded year adds column header, sorted works and footer', () => {
  const rows = buildStudioWorksRows(worksByYear, { 2024: true }, byTitle);

  assert.deepEqual(
    rows.map((row) => row.key),
    [
      'year-2024',
      'columns-2024',
      'work-2',
      'work-1',
      'footer-2024',
      'year-2023',
    ]
  );
});

test('the header row carries the sorted works for its year', () => {
  const [header] = buildStudioWorksRows(worksByYear, { 2024: true }, byTitle);

  assert.equal(header.type, 'year');
  assert.ok(header.type === 'year' && header.isExpanded);
  assert.deepEqual(
    header.type === 'year' && header.works.map((w) => w.id),
    [2, 1]
  );
});

test('a year with no works is left out even when expanded', () => {
  const rows = buildStudioWorksRows(worksByYear, { 2022: true }, byTitle);

  assert.ok(!rows.some((row) => row.year === 2022));
});
