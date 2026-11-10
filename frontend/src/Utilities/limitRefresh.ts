// A bulk operation sends an event per record, and on a large library a refresh can take
// many seconds. Replacing a running refresh doesn't stop the server working on it, so
// requests pile up and starve the operation that sent the events. Run one at a time with
// a gap after each, and once more afterwards if requests arrived in the meantime.
export default function limitRefresh(
  refresh: () => Promise<unknown>,
  gapMs = 5000
) {
  let isRefreshing = false;
  let isRefreshOwed = false;
  let skipGap = false;
  let endGap: (() => void) | undefined = undefined;

  function waitGap() {
    return new Promise<void>((resolve) => {
      endGap = resolve;
      setTimeout(resolve, gapMs);
    });
  }

  async function request() {
    if (isRefreshing) {
      isRefreshOwed = true;

      return;
    }

    isRefreshing = true;

    try {
      do {
        isRefreshOwed = false;

        await refresh();

        // After the refresh, not alongside it, so a slow query still leaves the server idle time
        if (!skipGap) {
          await waitGap();
        }

        endGap = undefined;
        skipGap = false;
      } while (isRefreshOwed);
    } finally {
      isRefreshing = false;
      endGap = undefined;
      skipGap = false;
    }
  }

  // Shows the final state as soon as an operation ends, rather than up to a gap later
  function flush() {
    if (!isRefreshing) {
      return;
    }

    skipGap = true;
    endGap?.();
  }

  return { request, flush };
}
