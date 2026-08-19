import { IBaseProps } from '../../../base';
import { MbscCalendarNavService } from '../../../shared/calendar-nav/calendar-nav';
import { IDragArgs, MbscCalendarCellData, MbscCalendarDayData, MbscCalendarEvent, MbscCalendarEventData, MbscResource, MbscSlot } from '../../../shared/calendar-view/calendar-view.types';
import { MbscDateType, MbscTimezonePlugin } from '../../../util/datetime.types.public';
import { IEventDragStopArgs, MbscEventConnection, MbscNewEventData, MbscResourceClickEvent, MbscResourceCreatedEvent, MbscResourceCreateEvent, MbscResourceDeletedEvent, MbscResourceDeleteEvent, MbscResourceDragEvent, MbscResourceOrderEvent, MbscSchedulerTimezone, MbscTimelineColumnWidth, MbscVirtualLoadEvent } from '../eventcalendar.types';
export interface ISTOptions extends IBaseProps {
    allDayText?: string;
    amText?: string;
    clickToCreate?: boolean | 'single' | 'double';
    checkSize?: number;
    connections?: MbscEventConnection[];
    colorsMap?: {
        [key: number]: MbscCalendarEvent[];
    };
    currentTimeIndicator?: boolean;
    columnWidth?: MbscTimelineColumnWidth;
    dayNames?: string[];
    dayNamesMin?: string[];
    dayNamesShort?: string[];
    dataTimezone?: string;
    dateFormat?: string;
    dateFormatFull?: string;
    dateFormatLong?: string;
    displayTimezone?: string;
    dragTimeStep?: number;
    dragStepAlign?: boolean;
    dragToCreate?: boolean;
    dragToMove?: boolean;
    dragToResize?: boolean;
    dragInTime?: boolean;
    dragBetweenResources?: boolean;
    dragBetweenSlots?: boolean;
    endDay: number;
    endTime?: string;
    eventHeight?: 'variable' | 'equal';
    eventMap?: {
        [key: string]: MbscCalendarEvent[];
    };
    eventOrder?: (event1: MbscCalendarEvent, event2: MbscCalendarEvent) => number;
    eventOverlap?: boolean;
    exclusiveEndDates?: boolean;
    extendDefaultEvent?: (args: MbscNewEventData) => MbscCalendarEvent | void;
    externalDrag?: boolean;
    externalDrop?: boolean;
    externalResourceDrag?: boolean;
    externalResourceDrop?: boolean;
    firstDay?: number;
    getDay?: (d: Date) => number;
    getDate?: (y: number, m: number, d: number, h?: number, i?: number, s?: number, u?: number) => Date;
    getMaxDayOfMonth?: (y: number, m: number) => number;
    getMonth?: (d: Date) => number;
    getWeekNumber?: (d: Date) => number;
    getYear?: (d: Date) => number;
    groupBy?: 'date' | 'resource';
    height?: number;
    hideEmpty?: boolean;
    hideInvalid?: boolean;
    immutableData?: boolean;
    invalidateEvent?: 'start-end' | 'strict';
    invalidsMap?: {
        [key: number]: MbscCalendarEvent[];
    };
    eventList?: boolean;
    maxDate?: MbscDateType;
    minDate?: MbscDateType;
    maxEventStack?: 'all' | 'auto' | number;
    minEventWidth?: number;
    monthNames?: string[];
    monthNamesShort?: string[];
    moreEventsPluralText?: string;
    moreEventsText?: string;
    navigateToEvent?: MbscCalendarEvent;
    navService: MbscCalendarNavService;
    nextIcon?: string;
    nextIconRtl?: string;
    downIcon?: string;
    newEventText?: string;
    pmText?: string;
    quarterText?: string;
    refDate?: MbscDateType;
    resources?: MbscResource[] | null | undefined;
    rowHeight?: 'variable' | 'equal';
    scroll: number;
    selected: number;
    selectedEventsMap: {
        [key: string]: MbscCalendarEvent;
    };
    showAllDay?: boolean;
    showDays?: boolean;
    showEventBuffer?: boolean;
    showEventTooltip?: boolean;
    startOffset?: number;
    endOffset?: number;
    viewDate: number;
    weekNumbers?: boolean;
    size?: number;
    resolution?: 'year' | 'quarter' | 'month' | 'week' | 'day' | 'hour';
    resolutionVertical?: 'day';
    resourceReorder?: boolean;
    slots?: MbscSlot[] | null | undefined;
    startDay: number;
    startTime?: string;
    timeCellStep: number;
    timeFormat?: string;
    timeLabelStep: number;
    timezonePlugin?: MbscTimezonePlugin;
    timezones?: Array<MbscSchedulerTimezone | string> | undefined;
    type: 'week' | 'day' | 'month' | 'year';
    virtualScroll?: boolean;
    weekText?: string;
    width?: number;
    zoomLevel?: string | number;
    eventDragEnd(args: IEventDragStopArgs): boolean;
    onActiveChange(args: any): void;
    onCellClick(args: any): void;
    onCellDoubleClick(args: any): void;
    onCellRightClick(args: any): void;
    onEventClick(args: any): void;
    onEventDragEnter?(args: any): void;
    onEventDragLeave?(args: any): void;
    onEventDoubleClick(args: any): void;
    onEventRightClick(args: any): void;
    onEventDelete(args: any): void;
    onEventHoverIn(args: any): void;
    onEventHoverOut(args: any): void;
    onEventDragEnd?(args: any, inst: any): void;
    onEventDragStart?(args: any, inst: any): void;
    onMoreClick?(args: any, inst: any): void;
    onPopoverClose?(args: any, inst: any): void;
    onResourceClick?(args: MbscResourceClickEvent, inst: any): void;
    onResourceDoubleClick?(args: MbscResourceClickEvent, inst: any): void;
    onResourceRightClick?(args: MbscResourceClickEvent, inst: any): void;
    onResourceCollapse?(args: any, inst: any): void;
    onResourceCreate?(args: MbscResourceCreateEvent, inst: any): void;
    onResourceCreated?(args: MbscResourceCreatedEvent, inst: any): void;
    onResourceDelete?(args: MbscResourceDeleteEvent, inst: any): void;
    onResourceDeleted?(args: MbscResourceDeletedEvent, inst: any): void;
    onResourceDragEnd?(args: MbscResourceDragEvent, inst: any): void;
    onResourceDragEnter?(args: MbscResourceDragEvent, inst: any): void;
    onResourceDragLeave?(args: MbscResourceDragEvent, inst: any): void;
    onResourceDragStart?(args: MbscResourceDragEvent, inst: any): void;
    onResourceExpand?(args: any, inst: any): void;
    onResourceOrderUpdate?(args: MbscResourceOrderEvent, inst: any): void;
    onScrollEnd?(args: any, inst: any): void;
    onVirtualLoading?(args: MbscVirtualLoadEvent): void;
    renderBufferAfter?(event: MbscCalendarEventData): any;
    renderBufferBefore?(event: MbscCalendarEventData): any;
    renderCell?(args: MbscCalendarCellData): any;
    renderDay?(args: MbscCalendarDayData): any;
    renderDayFooter?(args: MbscCalendarDayData): any;
    renderEventContent?(event: MbscCalendarEventData): any;
    renderEvent?(event: MbscCalendarEventData): any;
    renderHour?(args: MbscCalendarDayData): any;
    renderHourFooter?(args: MbscCalendarDayData): any;
    renderMonth?(args: MbscCalendarDayData): any;
    renderMonthFooter?(args: MbscCalendarDayData): any;
    renderResource?(resource: MbscResource, day?: Date): any;
    renderResourceEmpty?(day?: Date): any;
    renderResourceHeader?(): any;
    renderResourceFooter?(): any;
    renderSidebar?(resource: MbscResource, day?: Date): any;
    renderSidebarFooter?(): any;
    renderSidebarHeader?(): any;
    renderQuarter?(args: MbscCalendarDayData): any;
    renderQuarterFooter?(args: MbscCalendarDayData): any;
    renderWeek?(args: MbscCalendarDayData): any;
    renderWeekFooter?(args: MbscCalendarDayData): any;
    renderYear?(args: MbscCalendarDayData): any;
    renderYearFooter?(args: MbscCalendarDayData): any;
}
export interface ISTState {
    cellHeight?: number;
    cellWidth?: number;
    dayNameWidth?: number;
    dayWidth?: number;
    dragData?: IDragData;
    eventHeight?: number;
    footerHeight?: number;
    gridContWidth?: number;
    gridWidth?: number;
    gutterHeight?: number;
    hasScrollX?: boolean;
    hasScrollY?: boolean;
    headerHeight?: number;
    isTouchDrag?: boolean;
    parentRowHeight?: number;
    resourceColWidth?: number;
    resourceDepthStep?: number;
    rowHeight?: number;
    scrollContHeight?: number;
    scrollContWidth?: number;
    scrollBarSizeY?: number;
    update?: number;
    virtualX?: number;
    virtualStartY?: number;
    virtualEndY?: number;
}
export interface IDailyData {
    allDay: MbscCalendarEventData[];
    data: MbscCalendarEventData[];
    tracks?: number[];
    hasMore?: boolean;
}
export interface IEventPosData {
    bufferAfter?: string;
    bufferBefore?: string;
    cssClass?: string;
    start?: string;
    startDate: Date;
    startMs: number;
    end?: string;
    endDate: Date;
    endMs: number;
    offset?: number;
    position?: {
        height?: string;
        left?: string;
        right?: string;
        top?: string;
        width?: string;
    };
    shiftStartOffset?: number;
    shiftEndOffset?: number;
}
export interface IDragData {
    originDate?: number;
    /**
     * The dates of the event which is dragged.
     * We need to display the event boxes in case of touch drag, when we enter drag mode,
     * and continue to display during drag, but invisible, otherwise, if we loose the
     * element on which the touch started, the touch events will stop firing.
     */
    originDates?: {
        [key: string]: MbscCalendarEventData;
    };
    originResource?: number | string;
    hoverResource?: number | string;
    /** The dates of the dragged event. */
    draggedDates?: {
        [key: string]: MbscCalendarEventData;
    };
    /** The dragged event, displayed during drag. */
    draggedEvent?: MbscCalendarEventData;
    draggedResource?: MbscResource;
    draggedResourceVisible?: boolean;
    draggedResourceInvalid?: boolean;
    resource?: number | string;
    slot?: number | string;
    newDayIndex?: number;
}
export interface IResourceData {
    index: number;
    lastChildIndex: number;
    parentId?: string | number;
    siblings: MbscResource[];
}
interface IDataMap {
    [resourceId: string]: {
        [timestamp: string]: MbscCalendarEvent[];
        all: MbscCalendarEvent[];
    };
    all: {
        [timestamp: string]: MbscCalendarEvent[];
        all: MbscCalendarEvent[];
    };
}
export interface IDayData {
    columnTitle?: string;
    date: Date;
    endDate?: Date;
    dateIndex: number;
    dateKey: string;
    dateText: string;
    day: number;
    dayName: string;
    dayDiff: number;
    eventMap: IDataMap;
    colorMap: IDataMap;
    invalidMap: IDataMap;
    /** True when it is today or when today is in the week/month/year range - with higher resolutions  */
    isActive?: boolean;
    phStart?: number;
    phEnd?: number;
    label: string;
    lastOfMonth?: boolean;
    lastOfWeek?: boolean;
    monthIndex: number;
    monthText: string;
    monthTitle: string;
    times: Array<{
        t: number;
        first: boolean;
        last: boolean;
        i: number;
        hidden: boolean;
        dayLimit?: boolean;
    }>;
    timestamp: number;
    weekIndex: number;
    weekNr?: number;
    weekText: string;
    weekTitle: string;
    lengthStart?: number;
    lengthEnd?: number;
    width?: string;
}
export interface IColData {
    day: IDayData;
    hidden?: boolean;
    key: string;
    left: number;
    resource: MbscResource;
}
export interface IGroupData {
    stacks: MbscCalendarEventData[][];
    more: MbscCalendarEventData[];
}
export interface ICalendarDragArgs<T> extends IDragArgs {
    click?: boolean;
    dragData?: T;
    eventData?: MbscCalendarEventData;
    resource?: string | number;
    slot?: string | number;
}
export interface ICalendarEventDragArgs extends ICalendarDragArgs<MbscCalendarEvent> {
    dragData?: MbscCalendarEvent;
}
export interface ICalendarResourceDragArgs extends ICalendarDragArgs<MbscResource> {
    dragData?: MbscResource;
}
export {};
