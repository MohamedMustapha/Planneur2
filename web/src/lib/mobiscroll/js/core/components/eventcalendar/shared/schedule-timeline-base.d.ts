import { BaseComponent } from '../../../base';
import { MbscCalendarEvent, MbscCalendarEventData, MbscResource, MbscSlot } from '../../../shared/calendar-view/calendar-view.types';
import { MbscEventClickEvent, MbscSchedulerTimezone } from '../eventcalendar.types';
import { ICalendarEventDragArgs, ICalendarResourceDragArgs, IColData, IDailyData, IDayData, IEventPosData, ISTOptions, ISTState } from './schedule-timeline-base.types';
import * as i0 from "@angular/core";
/** @hidden */
export declare class STBase<PropType extends ISTOptions = ISTOptions, StateType extends ISTState = ISTState> extends BaseComponent<PropType, StateType> {
    _calcColWidths?: boolean;
    _calcRowHeights?: boolean;
    _colIndexMap: {
        [key: string]: number;
    };
    _colors: {
        [key: string]: {
            [key: string]: {
                [key: string]: IDailyData;
            };
        };
    };
    /** Array containing the columns to display */
    _cols: IDayData[];
    _colsNr: number;
    _colWidthsReal: {
        [key: string]: number;
    };
    /** Map containing the day index for each day; timestamp ->  dayIndex */
    _dayIndexMap: {
        [key: string]: number;
    };
    _dayNames: string[];
    /** Array containing the days to display */
    _days: IDayData[];
    /** Number of displayed days */
    _daysNr: number;
    _displayTime?: boolean;
    _dragTime?: number;
    _hiddenMap: {
        days: {
            [key: string]: boolean;
        };
        resources: {
            [key: string]: boolean;
        };
        data: {
            [key: string]: boolean;
        };
        emptyData: {
            [key: string]: boolean;
        };
        emptyDailyResources: {
            [key: string]: number;
        };
    };
    _endCellStyle?: {
        height?: string;
        width?: string;
    };
    /** Displayed end time as milliseconds since midnight */
    _endTime: number;
    _endTimeMs: number;
    _eventDropped: boolean;
    _eventHeight: number;
    _eventHeights: {
        [key: string]: number;
    };
    _eventMap: {
        [key: string]: MbscCalendarEventData;
    };
    _events: {
        [key: string]: {
            [key: string]: {
                [key: string]: IDailyData;
            };
        };
    };
    _firstDay: Date;
    _firstDayTz: Date;
    _fixedResourceTops: {
        [key: string]: number;
    };
    _gridWidth: number;
    _groupByResource?: boolean;
    _gridHeight: number;
    _hasHierarchy?: boolean;
    _hasResources?: boolean;
    _hasResY?: boolean;
    _hasSlots?: boolean;
    _hasSideSticky?: boolean;
    _headerDays: IDayData[];
    _invalids: {
        [key: string]: {
            [key: string]: {
                [key: string]: IDailyData;
            };
        };
    };
    _isDailyResolution?: boolean;
    _isMulti?: boolean;
    _isShifted: boolean;
    _isSingleResource: boolean;
    _isTimeline?: boolean;
    _lastDay: Date;
    _lastDayTz: Date;
    /** Contains the resources flatten out into one level */
    _resources: MbscResource[];
    _resourceDropped: boolean;
    /** Contains the map of resources flatten out into one level */
    _resourcesMap: {
        [key: string]: MbscResource;
    };
    _resourceWidth: number;
    _rowHeights: {
        [key: string]: string | undefined;
    };
    /** Contains the row heights in all cases (variable or equal row and event heights) */
    _rowHeightsAll: {
        [key: string]: number;
    };
    /** Contains the measured row heights in case of variable event heights */
    _rowHeightsReal: {
        [key: string]: number;
    };
    _scheduleHeaderHeight: number;
    _selectedDay?: number;
    _setRowHeight?: boolean;
    _showTimeIndicator?: boolean;
    _showCursorTime?: boolean;
    _startTimeOffset: number;
    _slots: MbscSlot[];
    _slotsMap: {
        [key: string]: MbscSlot;
    };
    _startCellStyle?: {
        height?: string;
        width?: string;
    };
    /** Displayed start time as milliseconds since midnight */
    _startTime: number;
    _stepCell: number;
    _stepLabel: number;
    /** Displayed time as milliseconds */
    _time: number;
    /** Array containing the hours to display */
    _timeLabels: Array<{
        [key: number]: string;
    }>;
    _times: number[];
    _timesBetween: number[];
    _timezones?: MbscSchedulerTimezone[];
    _variableEventHeight: boolean;
    _virtualRows: Array<{
        day?: IDayData;
        hidden?: boolean;
        rows: MbscResource[];
    }>;
    _virtualCols: Array<{
        day: IDayData;
        hidden?: boolean;
        resource: MbscResource;
        cols: IColData[];
        phStart: number;
        phEnd: number;
    }>;
    _yearFirst?: boolean;
    protected _allDayCont?: HTMLElement | null;
    protected _calcConnections?: boolean;
    protected _columns: IColData[];
    protected _cursorTimeCont?: HTMLElement | null;
    protected _draggedResourceEl?: HTMLElement | null;
    protected _draggedResourceLineEl?: HTMLElement | null;
    /** Number of event rows for a resource, used for row height calculation */
    protected _eventRows: {
        [key: string]: number;
    };
    protected _fixedHeight: number;
    protected _fixedResources: Array<{
        index: number;
        height: number;
        key: string;
        resource: MbscResource;
    }>;
    protected _gridCont?: HTMLElement | null;
    protected _footerCont?: HTMLElement | null;
    protected _headerCont?: HTMLElement | null;
    protected _isParentClick?: boolean;
    protected _isScrolling?: (newX?: number, newY?: number) => void;
    protected _resourceDepth: number;
    protected _resCont?: HTMLElement | null;
    protected _resourcesCopy: MbscResource[];
    protected _resourceTops: {
        [key: string]: number;
    };
    protected _columnLefts: {
        [key: string]: number;
    };
    protected _rows: Array<{
        dayIndex: number;
        key: string;
        resource: MbscResource;
        top: number;
    }>;
    protected _scrollCont?: HTMLElement | null;
    protected _scrollLeft: number;
    protected _scrollTop: number;
    protected _scrollX: number;
    protected _scrollY: number;
    protected _shouldAnimateScroll?: boolean;
    protected _shouldCheckSize?: boolean;
    protected _shouldCheckCell?: boolean;
    protected _shouldScroll?: boolean;
    protected _sidebarCont?: HTMLElement | null;
    protected _stickyFooter?: HTMLElement | null;
    protected _viewChanged: number;
    protected _visibleResources: MbscResource[];
    protected _endTimeOffset: number;
    protected _timeCont?: HTMLElement | null;
    private _allDayTop;
    private _clone;
    private _cursorX?;
    private _cursorY?;
    private _colHeight;
    private _colWidth;
    private _ctx;
    private _dragDayDelta;
    /**
     * This is duplicated in the state, but we store it as a property as well,
     * because we need to access it in the drag events immediately, and sometimes the state is not updated yet.
     * TODO: We should refactor this to use the state or the property only.
     */
    private _dragData?;
    private _dragDelta?;
    private _gridContBottom;
    private _gridContLeft;
    private _gridContRight;
    private _gridContTop;
    private _gridLeft;
    private _gridRight;
    private _gridTop;
    private _isCursorTimeVisible?;
    private _isTouch?;
    private _maxEventStack?;
    private _onCalendar?;
    private _reloadEvents?;
    private _reloadResources?;
    private _resourcesData;
    private _scrollAfterResize?;
    private _scrollStartTime;
    private _scrollTimer;
    private _scrollToMiddle?;
    private _tzCache;
    private _selectedChanged?;
    private _startColIndex;
    private _startDayIndex;
    private _startResourceIndex;
    private _startRow?;
    private _startSlotIndex?;
    private _expandTimer?;
    private _tempAllDay?;
    private _tempEnd?;
    private _tempEvent?;
    private _tempResource?;
    private _tempResourceData?;
    private _tempResourceNext?;
    private _tempSlot?;
    private _tempStart?;
    private _triggerCreated?;
    private _triggerDeleted?;
    private _touchTimer;
    private _unlisten?;
    private _unsubscribe?;
    private _viewDate?;
    _isToday(d: number): boolean;
    _toggleResource(resource: MbscResource, domEvent?: any): void;
    _onScroll: () => void;
    _onCellClick: (type: string, timestamp: number, v: number, domEvent: any, resource: string | number, slot?: string | number) => void;
    _onCellHover: (type: 'onCellHoverIn' | 'onCellHoverOut', timestamp: number, v: number, domEvent: any, resource: MbscResource, dayData: IDayData, slot?: MbscSlot) => void;
    _onResourceClick: (type: string, domEvent: any, resource: MbscResource, date?: Date) => void;
    _onResourceHover: (type: string, domEvent: any, resource: MbscResource, date?: Date) => void;
    _onMouseLeave: (ev?: any, force?: boolean) => void;
    _onMouseMove: (ev?: any) => void;
    _onEventClick: (args: MbscEventClickEvent) => void;
    _onResourceDragStart: (args: ICalendarResourceDragArgs, target?: HTMLElement) => void;
    _onResourceDragMove: (args: ICalendarResourceDragArgs) => void;
    _onResourceDragEnd: (args: ICalendarResourceDragArgs) => void;
    _onEventDragModeOn: (args: ICalendarEventDragArgs) => void;
    _onEventDragModeOff: (args: ICalendarEventDragArgs) => void;
    _onEventDragStart: (args: ICalendarEventDragArgs) => boolean;
    _onEventDragMove: (args: ICalendarEventDragArgs) => boolean;
    _onEventDragEnd: (args: ICalendarEventDragArgs) => void;
    _onExternalDrag: (args: ICalendarEventDragArgs) => void;
    protected _getEventPos(event: MbscCalendarEventData, day: Date, dateKey: string, displayedMap: Map<MbscCalendarEvent, boolean>, calculateMinWidth: boolean, schedulerAllDayEvent?: boolean): IEventPosData | undefined;
    protected _getEventData(event: MbscCalendarEvent, d: Date, resource?: MbscResource, slot?: MbscSlot, skipLabels?: boolean): MbscCalendarEventData;
    protected _getEvents(eventMap: {
        [key: string]: MbscCalendarEvent[];
    }): {
        [key: string]: {
            [key: string]: {
                [key: string]: IDailyData;
            };
        };
    };
    protected _getInvalids(invalidMap: {
        [key: string]: MbscCalendarEvent[];
    }): {
        [key: string]: {
            [key: string]: {
                [key: string]: IDailyData;
            };
        };
    };
    protected _getColors(colorMap: {
        [key: string]: MbscCalendarEvent[];
    }): {
        [key: string]: {
            [key: string]: {
                [key: string]: IDailyData;
            };
        };
    };
    protected _flattenResources(resources: MbscResource[] | null | undefined, flat: MbscResource[], depth: number, all?: boolean, copy?: MbscResource[], fixed?: boolean, parentId?: number | string): MbscResource[];
    protected _render(s: ISTOptions, state: ISTState): void;
    protected _mounted(): void;
    protected _updated(): void;
    protected _destroy(): void;
    /**
     * Calculates the start and middle dates displayed on the timeline, and sends it to the eventcalendar,
     * which will use it to restore the last viewed date on view change.
     * @param scrollPosX The horizontal scroll position of the view.
     * @param scrollPosY The vertical scroll position of the view.
     */
    protected _setViewDates(scrollPosX?: number, scrollPosY?: number): void;
    private _calcGridSizes;
    private _getScrollJump;
    private _getDragCol;
    private _getDragRow;
    private _getDragPos;
    private _getDragDates;
    /**
     * Returns a date with the time based on the coordinates on the grid.
     * @param day We're on this day.
     * @param posX X coord - for timeline.
     * @param posY Y coord - for scheduler.
     * @param dayIndex Index of the day on the timeline.
     * @param timeStep Time step in minutes.
     */
    private _getGridTime;
    private _mapEvents;
    private _scrollToTime;
    static ɵfac: i0.ɵɵFactoryDeclaration<STBase<any, any>, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<STBase<any, any>, "[mbsc-st-b]", never, {}, {}, never>;
}
