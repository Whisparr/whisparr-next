import throttle from 'lodash/throttle';
import { queryClient } from 'App/queryClient';

let isRefreshBlocked = false;
let hasPendingRefresh = false;

export function refreshQueue() {
  if (isRefreshBlocked) {
    hasPendingRefresh = true;
    return;
  }

  queryClient.invalidateQueries({ queryKey: ['/queue'] });
}

export function forceRefreshQueue() {
  hasPendingRefresh = false;
  queryClient.invalidateQueries({ queryKey: ['/queue'] });
}

export const throttledRefreshQueue = throttle(refreshQueue, 5000);

// While a selection or a modal is open, SignalR refreshes are held back and
// replayed once on release, so the rows do not shift under the user.
export function setQueueRefreshBlocked(isBlocked: boolean) {
  isRefreshBlocked = isBlocked;

  if (!isBlocked && hasPendingRefresh) {
    hasPendingRefresh = false;
    refreshQueue();
  }
}
