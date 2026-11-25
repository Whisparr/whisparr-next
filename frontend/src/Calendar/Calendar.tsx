import React, { useCallback, useEffect, useLayoutEffect, useRef } from 'react';
import * as commandNames from 'Commands/commandNames';
import { useCommandExecuting } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useCurrentPage from 'Helpers/Hooks/useCurrentPage';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import Agenda from './Agenda/Agenda';
import { useCalendarOption } from './calendarOptionsStore';
import CalendarDays from './Day/CalendarDays';
import DaysOfWeek from './Day/DaysOfWeek';
import CalendarHeader from './Header/CalendarHeader';
import useCalendar, { goToToday, useCalendarTime } from './useCalendar';
import styles from './Calendar.module.css';

const UPDATE_DELAY = 3600000; // 1 hour

function Calendar() {
  const requestCurrentPage = useCurrentPage();
  const updateTimeout = useRef<ReturnType<typeof setTimeout> | undefined>(
    undefined
  );

  const { isFetching, isFetched, error, refetch } = useCalendar();

  const view = useCalendarOption('view');
  const time = useCalendarTime();
  const contentRef = useRef<HTMLDivElement>(null);
  const stickyRef = useRef<HTMLDivElement>(null);

  const isRefreshingMovie = useCommandExecuting(commandNames.REFRESH_MOVIE);

  const wasRefreshingMovie = usePrevious(isRefreshingMovie);

  const handleScheduleUpdate = useCallback(() => {
    clearTimeout(updateTimeout.current);

    function updateCalendar() {
      goToToday();
      updateTimeout.current = setTimeout(updateCalendar, UPDATE_DELAY);
    }

    updateTimeout.current = setTimeout(updateCalendar, UPDATE_DELAY);
  }, []);

  useEffect(() => {
    handleScheduleUpdate();

    return () => {
      clearTimeout(updateTimeout.current);
    };
  }, [handleScheduleUpdate]);

  useEffect(() => {
    if (!requestCurrentPage) {
      goToToday();
    }
  }, [requestCurrentPage]);

  useEffect(() => {
    handleScheduleUpdate();
  }, [time, handleScheduleUpdate]);

  // Month view scrolls today's cell into view, which would leave it under the
  // sticky header. The header's height feeds the cell's scroll-margin-top.
  useLayoutEffect(() => {
    const content = contentRef.current;
    const sticky = stickyRef.current;

    if (!content || !sticky) {
      return;
    }

    const updateHeight = () =>
      content.style.setProperty(
        '--calendarStickyHeight',
        `${sticky.offsetHeight}px`
      );

    updateHeight();

    const observer = new ResizeObserver(updateHeight);
    observer.observe(sticky);

    return () => observer.disconnect();
  }, [isFetched, error, view]);

  useEffect(() => {
    if (wasRefreshingMovie && !isRefreshingMovie) {
      refetch();
    }
  }, [isRefreshingMovie, wasRefreshingMovie, refetch]);

  return (
    <div className={styles.calendar}>
      {isFetching && !isFetched ? <LoadingIndicator /> : null}

      {!isFetching && error ? (
        <Alert kind={kinds.DANGER}>{translate('CalendarLoadError')}</Alert>
      ) : null}

      {!error && isFetched && view === 'agenda' ? (
        <div className={styles.calendarContent}>
          <div className={styles.sticky}>
            <CalendarHeader />
          </div>
          <Agenda />
        </div>
      ) : null}

      {!error && isFetched && view !== 'agenda' ? (
        <div ref={contentRef} className={styles.calendarContent}>
          <div ref={stickyRef} className={styles.sticky}>
            <CalendarHeader />
            <DaysOfWeek />
          </div>
          <CalendarDays />
        </div>
      ) : null}
    </div>
  );
}

export default Calendar;
