import { AfterViewChecked, ElementRef, EventEmitter, TemplateRef } from '@angular/core';
import { EventcalendarBase } from '../../core/components/eventcalendar/eventcalendar';
import { MbscCellClickEvent, MbscCellHoverEvent, MbscEventcalendarOptions, MbscEventcalendarState, MbscEventcalendarView, MbscEventClickEvent, MbscEventConnection, MbscEventCreatedEvent, MbscEventCreateEvent, MbscEventCreateFailedEvent, MbscEventDeletedEvent, MbscEventDeleteEvent, MbscEventDragEvent, MbscEventList, MbscEventUpdatedEvent, MbscEventUpdateEvent, MbscEventUpdateFailedEvent, MbscLabelClickEvent, MbscNewEventData, MbscPageChangeEvent, MbscPageLoadedEvent, MbscPageLoadingEvent, MbscResourceClickEvent, MbscResourceCollapseEvent, MbscResourceCreatedEvent, MbscResourceCreateEvent, MbscResourceDeletedEvent, MbscResourceDeleteEvent, MbscResourceDragEvent, MbscResourceExpandEvent, MbscResourceHoverEvent, MbscResourceOrderEvent, MbscSelectedDateChangeEvent, MbscSelectedEventsChangeEvent, MbscVirtualLoadEvent } from '../../core/components/eventcalendar/eventcalendar.types';
import { ICalendarViewHost, MbscCalendarColor, MbscCalendarEvent, MbscCalendarEventData, MbscCalendarLabel, MbscCalendarMarked, MbscResource, MbscSlot } from '../../core/shared/calendar-view/calendar-view.types';
import { MbscDateType, MbscTimezonePlugin } from '../../core/util/datetime.types.public';
import { MbscCalendarView } from '../shared/calendar-view';
import { MbscInstanceService } from '../shared/instance.service';
import { MbscListItem } from './list-item';
import * as i0 from "@angular/core";
export declare class MbscEventListService {
    days: any;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscEventListService, never>;
    static ɵprov: i0.ɵɵInjectableDeclaration<MbscEventListService>;
}
export declare class MbscEventListDayDirective implements AfterViewChecked {
    private _el;
    private _els;
    timestamp: number;
    constructor(_el: ElementRef, _els: MbscEventListService);
    ngAfterViewChecked(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscEventListDayDirective, [null, { optional: true; }]>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscEventListDayDirective, "[mbsc-event-list-day]", never, { "timestamp": "timestamp"; }, {}, never>;
}
export declare class MbscEventListItem extends MbscListItem {
    date?: number;
    eventData: MbscCalendarEventData;
    hasDate: boolean;
    selected?: boolean;
    showColor?: boolean;
    source?: 'agenda' | 'popover';
    template?: TemplateRef<any>;
    eventContentTemplate?: TemplateRef<any>;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscEventListItem, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscEventListItem, "mbsc-event-list-item", never, { "date": "date"; "eventData": "eventData"; "hasDate": "hasDate"; "selected": "selected"; "showColor": "showColor"; "source": "source"; "template": "template"; "eventContentTemplate": "eventContentTemplate"; }, {}, never, never>;
}
/**
 * The Eventcalendar component.
 *
 * Usage:
 *
 * ```
 * <mbsc-eventcalendar></mbsc-eventcalendar>
 * ```
 */
export declare class MbscEventcalendar extends EventcalendarBase implements ICalendarViewHost {
    /** @hidden */
    vList?: ElementRef;
    /** @hidden */
    _calendarView: MbscCalendarView;
    /** {@inheritDoc MbscEventcalendarOptions.actionableEvents} */
    actionableEvents?: boolean;
    /**
     * Template to customize the agenda empty state.
     * Should contain the desired template to be used in the agenda when the events list is empty.
     *
     * @group Renderers
     * @group Renderers_agenda
     */
    agendaEmptyTemplate?: TemplateRef<any>;
    /**
     * Template to customize agenda listing.
     *
     * There is a scrolling functionality of the list: when clicking on a calendar date scrolls to the date on the list as well,
     * and scrolling the list updates the selected date on the calendar.
     * For this functionality to work, you need to use the `mbsc-event-list-day` directive on the elements of
     * the days in your template with the day timestamp as parameter:
     *
     * ```html
     * <mbsc-eventcalendar [agendaTemplate]="myTemplate">
     *   <ng-template #myTemplate let-data>
     *     <ul *ngFor="let day of data; trackBy: getDayKey" mbsc-event-list-day [timestamp]="day.timestamp">
     *       <li>{{day.date}}</li>
     *       <li *ngFor="let event of day.events; trackBy: getEventKey">
     *         {{event.title}}
     *       </li>
     *     </ul>
     *   </ng-template>
     * </mbsc-eventcalendar>
     * ```
     *
     * The template data is an array containing the events of the current view, grouped by day. A day object has the following properties:
     * - `date`: String - The formatted date of the day.
     * - `events`: Array - The list of events for the day. An event object has the following properties:
     *    - `allDay`: String - The localized all-day text in case of all day events.
     *    - `end`: String - The formatted end time, if the event is not all day.
     *    - `id`: String - The id of the event.
     *    - `isMultiDay`: Boolean - True if the event spans across multiple days.
     *    - `lastDay`: Boolean - True if it's rendered on the last day of a multiple event.
     *    - `original`: Object - The original event object.
     *    - `start`: String - The formatted start time, if the event is not all day.
     *    - `title`: String - The title of the event.
     * - `timestamp`: Number - The timestamp of the day
     *
     * @group Renderers
     * @group Renderers_agenda
     */
    agendaTemplate?: TemplateRef<any>;
    /**
     * Template to customize the events that appear on the agenda.
     *
     * If you are looking to customize only the content (e.g. add custom elements) and don't want to bother with the styling of the event,
     * you can use the [agendaEventContentTemplate](#template-agendaEventContentTemplate) option.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_agenda
     *
     */
    agendaEventTemplate?: TemplateRef<any>;
    /**
     * Template to customize the event content that appears on the agenda.
     * The Eventcalendar will take care of styling and you can focus on what you show inside of the event.
     *
     * If you are looking to fully customize the event (e.g. add custom hover effects) you will need to use the
     * [agendaEventTemplate](#template-agendaEventTemplate) option.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object. Any custom property on the event can be accessed through this property.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_agenda
     *
     */
    agendaEventContentTemplate?: TemplateRef<any>;
    /**
     * Template reference to fully customize the buffer area that is displayed at the end of the scheduler and timeline events.
     * The buffer can be defined with the help of the `bufferAfter` property of the [event data](#opt-data).
     *
     * The template will receive an event object as data.
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     * - `currentResource`: _string | number_ - The resource of the row or column where the event is being rendered.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_scheduler
     * @group Renderers_timeline
     */
    bufferAfterTemplate?: TemplateRef<any>;
    /**
     * Template reference to fully customize the buffer area that is displayed at the start of the scheduler and timeline events.
     * The buffer can be defined with the help of the `bufferBefore` property of the [event data](#opt-data).
     *
     * The template will receive an event object as data.
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     * - `currentResource`: _string | number_ - The resource of the row or column where the event is being rendered.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_scheduler
     * @group Renderers_timeline
     */
    bufferBeforeTemplate?: TemplateRef<any>;
    /**
     * Template to customize the event contents, that appears on the calendar.
     * The Eventcalendar will take care of styling and you can focus on what you show inside of the event.
     *
     * If you are looking to fully customize the event (ex. add custom hover effects) you will need to use the
     * [calendarEventTemplate](#template-calendarEventTemplate) option.
     * In that case you will only get the positioning done by the Eventcalendar and everything else is up to you.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ -  The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `original`: _MbscCalendarEvent_ - The original event object. Any custom property on the event can be accessed through this property.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_calendarview
     */
    calendarEventContentTemplate?: TemplateRef<any>;
    /**
     * Customize the events that appear on the calendar view.
     * The Eventcalendar will take care of the positioning, but everything else (like background color, hover effect, etc.) is left to you.
     *
     * If you are looking to customize only the content (e.g. add custom elements) and
     * don't want to bother with the styling of the event, you can use the
     * [calendarEventContentTemplate](#template-calendarEventContentTemplate) option.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ -  The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `original`: _MbscCalendarEvent_ - The original event object. Any custom property on the event can be accessed through this property.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     *
     * @defaultValue undefined
     * @group Renderers
     * @group Renderers_calendarview
     */
    calendarEventTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.calendarSystem} */
    calendarSystem?: any;
    /**
     * Customize the cells of the Eventcalendar in scheduler and timeline views.
     *
     * The template will receive an object as context, which can be used to display custom content in the cells.
     * Keep the cell template simple, as it will be called and rendered **very frequently** during scroll and view changes.
     *
     * The object passed to the template contains the following properties:
     * - `colors`: _Array<MbscCalendarColor>_ - The list of colors for the current cell.
     * - `date`: _Date_ – The current cell's date.
     * - `events`: _Array<MbscCalendarEvent>_ List of events for the current cell.
     * - `invalids`: _Array<MbscCalendarInvalid>_ - The list of invalids for the current cell.
     * - `resource`: _MbscResource_ - The resource object of the current cell.
     * - `slot`: _MbscSlot_ - The slot object of the rendered cell.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_scheduler
     * @group Renderers_timeline
     */
    cellTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.clickToCreate} */
    clickToCreate?: boolean | 'double' | 'single';
    /** {@inheritDoc MbscEventcalendarOptions.colors} */
    colors?: MbscCalendarColor[];
    /** {@inheritDoc MbscEventcalendarOptions.connections} */
    connections?: MbscEventConnection[];
    /** {@inheritDoc MbscEventcalendarOptions.data} */
    data?: MbscCalendarEvent[];
    /**
     * @deprecated Use the `agendaDayTemplate`, `calendarDayTemplate`, `schedulerDayTemplate` or `timelineDayTemplate` property instead.
     * */
    dayTemplate?: TemplateRef<any>;
    /**
     * Customize the day cells of the agenda view.
     *
     * The template will receive an object as data. This data can be used to show day specific things on the Eventcalendar.
     * The object passed to the template has the following properties:
     * - `date`: Date object - The specific date as a Date object.
     * - `events`: Array - The list of events of the day.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_agenda
     */
    agendaDayTemplate?: TemplateRef<any>;
    /**
     * Customize the day cells of the calendar view.
     *
     * If you are looking to customize only the day cells content and don't want to bother with the styling of the event,
     * in case of calendar and scheduler views you can use the [calendarDayContentTemplate](#template-calendarDayContentTemplate) option.
     *
     * The template will receive an object as data. This data can be used to show day specific things on the Eventcalendar.
     * The object passed to the template has the following properties:
     * - `date`: Date object - The specific date as a Date object.
     * - `selected`: Boolean - True if the date is selected.
     * - `events`: Array - The list of events of the day.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_calendarview
     */
    calendarDayTemplate?: TemplateRef<any>;
    /**
     * Customize the day cells of the scheduler view.
     *
     * If you are looking to customize only the day cells content and don't want to bother with the styling of the event,
     * in case of calendar and scheduler views you can use the [schedulerDayContentTemplate](#template-schedulerDayContentTemplate) option.
     *
     * The template will receive an object as data. This data can be used to show day specific things on the Eventcalendar.
     * The object passed to the template has the following properties:
     * - `date`: Date object - The specific date as a Date object.
     * - `events`: Array - The list of events of the day.
     * - `resource`: String, Number - The id of the resource in case of scheduler(week and month views)
     * when the events are grouped by resources.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_scheduler
     */
    schedulerDayTemplate?: TemplateRef<any>;
    /**
     * Customize the day cells of the calendar view and the date header in case of scheduler and timeline views.
     *
     * The template will receive an object as data. This data can be used to show day specific things on the Eventcalendar.
     * The object passed to the template has the following properties:
     * - `date`: Date object - The specific date as a Date object.
     * - `events`: Array - The list of events of the day.
     * - `isActive`: Boolean - This parameter will be true when rendering the current day/week/month/year
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    timelineDayTemplate?: TemplateRef<any>;
    /**
     * @deprecated @ Use the `calendarDayContentTemplate` or `schedulerDayContentTemplate` property instead.
     * */
    dayContentTemplate?: TemplateRef<any>;
    /**
     * Customize the day cells content of the event calendar. The Eventcalendar will take care of the styling and you can focus on
     * what you show beside the day number.
     *
     * If you are looking to fully customize the day (e.g. add custom hover effects) you will need to use the
     * [calendarDayTemplate](#template-calendarDayTemplate) option.
     *
     * The template will receive an object as data. This data can be used to show day specific things on the Eventcalendar.
     * The object passed to the template has the following properties:
     * - `date`: Date object - The specific date as a Date object.
     * - `selected`: Boolean - True if the date is selected.
     * - `events`: Array - The list of events of the day.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_calendarview
     */
    calendarDayContentTemplate?: TemplateRef<any>;
    /**
     * Customize the day cells content of the event calendar. The Eventcalendar will take care of the styling and you can focus on
     * what you show beside the day number.
     *
     * If you are looking to fully customize the day (e.g. add custom hover effects) you will need to use the
     * [schedulerDayTemplate](#template-schedulerDayTemplate) option.
     *
     * The template will receive an object as data. This data can be used to show day specific things on the Eventcalendar.
     * The object passed to the template has the following properties:
     * - `date`: Date object - The specific date as a Date object.
     * - `events`: Array - The list of events of the day.
     * - `resource`: String, Number - The id of the resource in case of scheduler(week and month views)
     * when the events are grouped by resources.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_scheduler
     */
    schedulerDayContentTemplate?: TemplateRef<any>;
    /**
     * Template to customize the footer of each day for the timeline.
     *
     * The following day specific details are available:
     * - `date`: _Date_ - The date of the day.
     * - `events`: _Array<MbscCalendarEvent>_ - The list of events for the day.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    dayFooterTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.dataTimezone} */
    dataTimezone?: string;
    /** {@inheritDoc MbscEventcalendarOptions.displayTimezone} */
    displayTimezone?: string;
    /** {@inheritDoc MbscEventcalendarOptions.dragBetweenResources} */
    dragBetweenResources?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.dragBetweenSlots} */
    dragBetweenSlots?: boolean;
    dragInTime?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.dragTimeStep} */
    dragTimeStep?: number;
    /** {@inheritDoc MbscEventcalendarOptions.dragTimeStepBase} */
    dragTimeStepBase?: 'dayStart' | 'viewStart';
    /** {@inheritDoc MbscEventcalendarOptions.dragToCreate} */
    dragToCreate?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.dragToResize} */
    dragToResize?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.dragToMove} */
    dragToMove?: boolean;
    /**
     * @deprecated Use the `agendaEventContentTemplate` and `popoverEventContentTemplate` properties instead.
     */
    eventContentTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.eventDelete} */
    eventDelete?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.} */
    eventOrder?: (event1: MbscCalendarEvent, event2: MbscCalendarEvent) => number;
    /** {@inheritDoc MbscEventcalendarOptions.eventOverlap} */
    eventOverlap?: boolean;
    /**
     * @deprecated Use the `agendaEventTemplate` and `popoverEventTemplate` properties instead.
     */
    eventTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.exclusiveEndDates} */
    exclusiveEndDates?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.extendDefaultEvent} */
    extendDefaultEvent?: (args: MbscNewEventData) => MbscCalendarEvent | void;
    /** {@inheritDoc MbscEventcalendarOptions.externalDrag} */
    externalDrag?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.externalDrop} */
    externalDrop?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.externalResourceDrag} */
    externalResourceDrag?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.externalResourceDrop} */
    externalResourceDrop?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.groupBy} */
    groupBy?: 'date' | 'resource';
    /**
     * Template to customize the header of the event calendar.
     * You can use custom markup or components as well as the built in header
     * components of the calendar.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     */
    headerTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.height} */
    height?: number | string;
    /**
     * Template to customize the header of the hour columns on the timeline view.
     *
     * The following properties are available:
     * - `date`: _Date_ - The date and time of the rendered hour.
     * - `events`: _Array<MbscCalendarEvent>_ - The list of events for the hour.
     * - `isActive`: _boolean_ - True for the current hour.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    hourTemplate?: TemplateRef<any>;
    /**
     * Template to customize the footer of the hour columns on the timeline view.
     *
     * The following properties are available:
     * - `date`: _Date_ - The date and time of the rendered hour.
     * - `events`: _Array<MbscCalendarEvent>_ - The list of events for the hour.
     * - `isActive`: _boolean_ - True for the current hour.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    hourFooterTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.immutableData} */
    immutableData?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.invalid} */
    invalid?: any[];
    /** {@inheritDoc MbscEventcalendarOptions.invalidateEvent} */
    invalidateEvent?: 'start-end' | 'strict';
    /**
     * @deprecated Use the `calendarEventContentTemplate` property instead.
     */
    labelContentTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.labels} */
    labels?: MbscCalendarLabel[];
    /**
     * @deprecated Use the `calendarEventTemplate` property instead.
     */
    labelTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.marked} */
    marked?: MbscCalendarMarked[];
    /** {@inheritDoc MbscEventcalendarOptions.max} */
    max?: MbscDateType;
    /** {@inheritDoc MbscEventcalendarOptions.min} */
    min?: MbscDateType;
    /** {@inheritDoc MbscEventcalendarOptions.mousewheel} */
    mousewheel?: boolean;
    /**
     * Template to customize the header of the month column on the timeline view.
     *
     * The following properties are available:
     *  - `date`: _Date_ - First day of the rendered month.
     *  - `events`: _Array<MbscCalendarEvent>_ - The list of events for the month.
     *  - `isActive`: _boolean_ - True for the current month.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    monthTemplate?: TemplateRef<any>;
    /**
     * Template to customize the footer of the month column on the timeline view.
     *
     * The following properties are available:
     *  - `date`: _Date_ - First day of the rendered month.
     *  - `events`: _Array<MbscCalendarEvent>_ - The list of events for the month.
     *  - `isActive`: _boolean_ - True for the current month.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    monthFooterTemplate?: TemplateRef<any>;
    /**
     * Template to customize the events that appear on the popover.
     *
     * If you are looking to customize only the content (e.g. add custom elements) and don't want to bother with the styling of the event,
     * you can use the [popoverEventContentTemplate](#template-popoverEventContentTemplate) option.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_calendarview
     *
     */
    popoverEventTemplate?: TemplateRef<any>;
    /**
     * Template to customize the event content that appears on the popover.
     * The Eventcalendar will take care of styling and you can focus on what you show inside of the event.
     *
     * If you are looking to fully customize the event (e.g. add custom hover effects) you will need to use the
     * [popoverEventTemplate](#template-popoverEventTemplate) option.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object. Any custom property on the event can be accessed through this property.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_calendarview
     *
     */
    popoverEventContentTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.refDate} */
    refDate?: MbscDateType;
    /** {@inheritDoc MbscEventcalendarOptions.resources} */
    resources?: MbscResource[] | null;
    /**
     * Template to customize the cell content above the resource column on the timeline view.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    resourceHeaderTemplate?: TemplateRef<any>;
    /**
     * Template to customize the cell content below the resource column on the timeline view.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    resourceFooterTemplate?: TemplateRef<any>;
    /**
     * Template to customize the resource template of the scheduler and timeline views.
     *
     * The following properties are available:
     *  - `resource`: _MbscResource_ - The rendered resource.
     *  - `day`: _Date_ - The date on which the resource is rendered.
     *  Available when grouping by date in the scheduler view,
     *  or when vertical day resolution is used in the timeline view.
     *
     * ```html
     * <mbsc-eventcalendar [resourceTemplate]="myTemplate">
     *   <ng-template #myTemplate let-resource>
     *     <div>{{resource.name}}</div>
     *     <p>{{resource.description}}</p>
     *     <img [src]="resource.img" />
     *   </ng-template>
     * </mbsc-eventcalendar>
     * ```
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_scheduler
     * @group Renderers_timeline
     */
    resourceTemplate?: TemplateRef<any>;
    /**
     * Template to customize the content of the resource column in case of an empty resource array.
     *
     * It will receive the following parameter:
     * - `day`: _Date_ - The date on which the resource is rendered in case of vertical day resolution.
     *
     * ```html
     * <mbsc-eventcalendar [resourceEmptyTemplate]="myEmptyTemplate">
     *   <ng-template #myEmptyTemplate>
     *     <!-- content -->
     *   </ng-template>
     * </mbsc-eventcalendar>
     * ```
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    resourceEmptyTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.slots} */
    slots?: MbscSlot[] | null;
    /**
     * Template to customize the slot template of the timeline view.
     *
     * It will receive an object as parameter that has the following properties:
     * - `date`: _Date_ - The specific date where the slot is rendered.
     * - `slot`: _MbscSlot_ - Data of the rendered slot.
     *
     * ```
     * <mbsc-eventcalendar [slotTemplate]="myTemplate">
     *   <ng-template #myTemplate let-slotdata>
     *     <div>{{slotdata.slot.name}}</div>
     *     <p>{{slotdata.slot.description}}</p>
     *     <img [src]="slotdata.slot.img" />
     *   </ng-template>
     * </mbsc-eventcalendar>
     * ```
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    slotTemplate?: TemplateRef<any>;
    /**
     * @deprecated Use the `schedulerEventContentTemplate` and `timelineEventContentTemplate` properties instead.
     */
    scheduleEventContentTemplate?: TemplateRef<any>;
    /**
     * @deprecated Use the `schedulerEventTemplate` and `timelineEventTemplate` properties instead.
     */
    scheduleEventTemplate?: TemplateRef<any>;
    /**
     * Template to customize the event content that appears on the scheduler.
     * The Eventcalendar will take care of styling and you can focus on what you show inside of the event.
     *
     * If you are looking to fully customize the event (e.g. add custom hover effects) you will need to use
     * the [schedulerEventTemplate](#template-schedulerEventTemplate) option. In that case you will only get
     * the positioning done by the Eventcalendar and everything else is up to you.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     * - `currentResource`: _string | number_ - The resource of the row or column where the event is being rendered.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_scheduler
     */
    schedulerEventContentTemplate?: TemplateRef<any>;
    /**
     * Template to customize the events that appear on the scheduler.
     * The Eventcalendar will take care of the positioning,
     * but everything else (like background color, hover effect, etc.) is left to you.
     *
     * If you are looking to customize only the content (e.g. add custom elements) and don't want to bother with the styling of the event,
     * you can use the [schedulerEventContentTemplate](#template-schedulerEventContentTemplate) option.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     * - `currentResource`: _string | number_ - The resource of the row or column where the event is being rendered.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_scheduler
     */
    schedulerEventTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.selectedDate} */
    selectedDate: MbscDateType;
    /** {@inheritDoc MbscEventcalendarOptions.onSelectedDateChange} */
    selectedDateChange: EventEmitter<MbscDateType>;
    /** {@inheritDoc MbscEventcalendarOptions.selectedEvents} */
    selectedEvents?: MbscCalendarEvent[];
    /** {@inheritDoc MbscEventcalendarOptions.onSelectedEventsChange} */
    selectedEventsChange: EventEmitter<MbscCalendarEvent[]>;
    /** {@inheritDoc MbscEventcalendarOptions.selectMultipleEvents} */
    selectMultipleEvents?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.showControls} */
    showControls?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.showEventBuffer} */
    showEventBuffer?: boolean;
    /** {@inheritDoc MbscEventcalendarOptions.showEventTooltip} */
    showEventTooltip?: boolean;
    /**
     * Template to customize the empty cell content below the sidebar column.
     * This template is only rendered for the timeline view, if the [sidebarTemplate](#template-sidebarTemplate) is present.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    sidebarFooterTemplate?: TemplateRef<any>;
    /**
     * Template to customize the empty cell content above the sidebar column.
     * This template is only rendered for the timeline view, if the [sidebarTemplate](#template-sidebarTemplate) is present.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    sidebarHeaderTemplate?: TemplateRef<any>;
    /**
     * Template to customize and add a sidebar on the right side of the timeline.
     *
     * The template will receive the resource object as data. This data can be used to show resource specific things on the sidebar.
     *
     * It will receive the following parameters:
     * - `resource`: _MbscResource_ - The rendered resource.
     * - `day`: _Date_ - The date on which the resource is rendered in case of vertical day resolution in the timeline view.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    sidebarTemplate?: TemplateRef<any>;
    /**
     * Template to customize the header of the quarter columns on the timeline view.
     *
     * The following properties are available:
     * - `date`: _Date_ - First day of the rendered quarter.
     * - `events`: _Array<MbscCalendarEvent>_ - The list of events for the quarter.
     * - `isActive`: _boolean_ - True for the current quarter.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    quarterTemplate?: TemplateRef<any>;
    /**
     * Template to customize the footer of the quarter column on the timeline view.
     *
     * The following properties are available:
     * - `date`: _Date_ - First day of the rendered quarter.
     * - `events`: _Array<MbscCalendarEvent>_ - The list of events for the quarter.
     * - `isActive`: _boolean_ - True for the current quarter.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    quarterFooterTemplate?: TemplateRef<any>;
    /**
     * {@inheritDoc MbscEventcalendarOptions.quarterText}
     * @group Localizations
     * @group Localizations_timeline
     */
    quarterText?: string;
    /**
     * Template to customize the event content that appears on the timeline.
     * The Eventcalendar will take care of styling and you can focus on what you show inside of the event.
     *
     * If you are looking to fully customize the event (e.g. add custom hover effects) you will need to use
     * the [timelineEventTemplate](#template-timelineEventTemplate) option. In that case you will only get
     * the positioning done by the Eventcalendar and everything else is up to you.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     * - `currentResource`: _string | number_ - The resource of the row or column where the event is being rendered.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    timelineEventContentTemplate?: TemplateRef<any>;
    /**
     * Template to customize the events that appear on the timeline.
     * The Eventcalendar will take care of the positioning,
     * but everything else (like background color, hover effect, etc.) is left to you.
     *
     * If you are looking to customize only the content (e.g. add custom elements) and don't want to bother with the styling of the event,
     * you can use the [timelineEventContentTemplate](#template-timelineEventContentTemplate) option.
     *
     * Some of the event specific details are computed, but there is also a reference to the **original** event they come from.
     * The following event specific properties are available:
     * - `allDay`: _string_ - The localized all-day text in case of all day events.
     * - `end`: _string_ - The formatted end time, if the event is not all day.
     * - `id`: _string_ - The id of the event.
     * - `isMultiDay`: _boolean_ - True if the event spans across multiple days.
     * - `lastDay`: _boolean_ - True if it's rendered on the last day of a multiple event.
     * - `original`: _MbscCalendarEvent_ - The original event object.
     * - `start`: _string_ - The formatted start time, if the event is not all day.
     * - `title`: _string_ - The title of the event.
     * - `currentResource`: _string | number_ - The resource of the row or column where the event is being rendered.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    timelineEventTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.valid} */
    valid?: any[];
    /** {@inheritDoc MbscEventcalendarOptions.view} */
    view?: MbscEventcalendarView;
    /** {@inheritDoc MbscEventcalendarOptions.width} */
    width?: number | string;
    /**
     * Template to customize the header of the week column on the timeline view.
     *
     * The following properties are available:
     *  - `date`: _Date_ - First day of the rendered week.
     *  - `events`: _Array<MbscCalendarEvent>_ - The list of events for the week.
     *  - `isActive`: _boolean_ - True for the current week.
     *  - `endDate`: _Date_ - The end date of the week.
     *  - `startDate`: _Date_ - The start date of the week.
     *  - `weekNr`: _number_ - The week number. Enumeration starts with the first week of the year.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    weekTemplate?: TemplateRef<any>;
    /**
     * Template to customize the footer of the week column on the timeline view.
     *
     * The following properties are available:
     *  - `date`: _Date_ - First day of the rendered week.
     *  - `events`: _Array<MbscCalendarEvent>_ - The list of events for the week.
     *  - `isActive`: _boolean_ - True for the current week.
     *  - `endDate`: _Date_ - The end date of the week.
     *  - `startDate`: _Date_ - The start date of the week.
     *  - `weekNr`: _number_ - The week number. Enumeration starts with the first week of the year.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    weekFooterTemplate?: TemplateRef<any>;
    /**
     * Template to customize the header of the year column on the timeline view.
     *
     * The following properties are available:
     *  - `date`: _Date_ - First day of the rendered year.
     *  - `events`: _Array<MbscCalendarEvent>_ - The list of events for the year.
     *  - `isActive`: _boolean_ - True for the current year.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    yearTemplate?: TemplateRef<any>;
    /**
     * Template to customize the footer of the year column on the timeline view.
     *
     * The following properties are available:
     *  - `date`: _Date_ - First day of the rendered year.
     *  - `events`: _Array<MbscCalendarEvent>_ - The list of events for the year.
     *  - `isActive`: _boolean_ - True for the current year.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     * @group Renderers_timeline
     */
    yearFooterTemplate?: TemplateRef<any>;
    /** {@inheritDoc MbscEventcalendarOptions.zoomLevel} */
    zoomLevel?: string | number;
    /**
     * {@inheritDoc MbscEventcalendarOptions.allDayText}
     * @group Localizations
     */
    allDayText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.amText}
     * @group Localizations
     */
    amText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.dateFormat}
     * @group Localizations
     */
    dateFormat?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.dateFormatFull}
     * @group Localizations
     */
    dateFormatFull?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.dateFormatLong}
     * @group Localizations
     */
    dateFormatLong?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.dayNames}
     * @group Localizations
     */
    dayNames?: string[];
    /**
     * {@inheritDoc MbscEventcalendarOptions.dayNamesMin}
     * @group Localizations
     */
    dayNamesMin?: string[];
    /**
     * {@inheritDoc MbscEventcalendarOptions.dayNamesShort}
     * @group Localizations
     */
    dayNamesShort?: string[];
    /**
     * {@inheritDoc MbscEventcalendarOptions.eventsText}
     * @group Localizations
     */
    eventsText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.eventText}
     * @group Localizations
     */
    eventText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.firstDay}
     * @group Localizations
     */
    firstDay?: number;
    /**
     * {@inheritDoc MbscEventcalendarOptions.monthNames}
     * @group Localizations
     */
    monthNames?: string[];
    /**
     * {@inheritDoc MbscEventcalendarOptions.monthNamesShort}
     * @group Localizations
     */
    monthNamesShort?: string[];
    /**
     * {@inheritDoc MbscEventcalendarOptions.moreEventsPluralText}
     * @group Localizations
     */
    moreEventsPluralText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.moreEventsText}
     * @group Localizations
     */
    moreEventsText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.newEventText}
     * @group Localizations
     */
    newEventText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.nextPageText}
     * @group Localizations
     */
    nextPageText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.noEventsText}
     * @group Localizations
     */
    noEventsText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.pmText}
     * @group Localizations
     */
    pmText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.prevPageText}
     * @group Localizations
     */
    prevPageText?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.timeFormat}
     * @group Localizations
     */
    timeFormat?: string;
    /**
     * {@inheritDoc MbscEventcalendarOptions.timezonePlugin}
     * @group Localizations
     */
    timezonePlugin?: MbscTimezonePlugin;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onCellClick}
     */
    onCellClick: EventEmitter<MbscCellClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onCellDoubleClick}
     */
    onCellDoubleClick: EventEmitter<MbscCellClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onCellRightClick}
     */
    onCellRightClick: EventEmitter<MbscCellClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onCellHoverIn}
     */
    onCellHoverIn: EventEmitter<MbscCellHoverEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onCellHoverOut}
     */
    onCellHoverOut: EventEmitter<MbscCellHoverEvent>;
    onResourceHoverIn: EventEmitter<MbscResourceHoverEvent>;
    onResourceHoverOut: EventEmitter<MbscResourceHoverEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventClick}
     */
    onEventClick: EventEmitter<MbscEventClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventDoubleClick}
     */
    onEventDoubleClick: EventEmitter<MbscEventClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventRightClick}
     */
    onEventRightClick: EventEmitter<MbscEventClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventHoverIn}
     */
    onEventHoverIn: EventEmitter<MbscEventClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventHoverOut}
     */
    onEventHoverOut: EventEmitter<MbscEventClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventCreate}
     */
    onEventCreate: EventEmitter<MbscEventCreateEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventCreated}
     */
    onEventCreated: EventEmitter<MbscEventCreatedEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventDelete}
     */
    onEventDelete: EventEmitter<MbscEventDeleteEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventDeleted}
     */
    onEventDeleted: EventEmitter<MbscEventDeletedEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventDragEnd}
     */
    onEventDragEnd: EventEmitter<MbscEventDragEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventDragStart}
     */
    onEventDragStart: EventEmitter<MbscEventDragEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventUpdate}
     */
    onEventUpdate: EventEmitter<MbscEventUpdateEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventUpdated}
     */
    onEventUpdated: EventEmitter<MbscEventUpdatedEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventCreateFailed}
     */
    onEventCreateFailed: EventEmitter<MbscEventCreateFailedEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventUpdateFailed}
     */
    onEventUpdateFailed: EventEmitter<MbscEventUpdateFailedEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventDragEnter}
     */
    onEventDragEnter: EventEmitter<MbscEventDragEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onEventDragLeave}
     */
    onEventDragLeave: EventEmitter<MbscEventDragEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onLabelClick}
     */
    onLabelClick: EventEmitter<MbscLabelClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onSelectedEventsChange}
     */
    onSelectedEventsChange: EventEmitter<MbscSelectedEventsChangeEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onPageChange}
     */
    onPageChange: EventEmitter<MbscPageChangeEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onPageLoaded}
     */
    onPageLoaded: EventEmitter<MbscPageLoadedEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onPageLoading}
     */
    onPageLoading: EventEmitter<MbscPageLoadingEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceClick}
     */
    onResourceClick: EventEmitter<MbscResourceClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceCollapse}
     */
    onResourceCollapse: EventEmitter<MbscResourceCollapseEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceDoubleClick}
     */
    onResourceDoubleClick: EventEmitter<MbscResourceClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceExpand}
     */
    onResourceExpand: EventEmitter<MbscResourceExpandEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceDragStart}
     */
    onResourceDragStart: EventEmitter<MbscResourceDragEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceCreate}
     */
    onResourceCreate: EventEmitter<MbscResourceCreateEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceCreated}
     */
    onResourceCreated: EventEmitter<MbscResourceCreatedEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceDelete}
     */
    onResourceDelete: EventEmitter<MbscResourceDeleteEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceDeleted}
     */
    onResourceDeleted: EventEmitter<MbscResourceDeletedEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceDragEnd}
     */
    onResourceDragEnd: EventEmitter<MbscResourceDragEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceDragEnter}
     */
    onResourceDragEnter: EventEmitter<MbscResourceDragEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceDragLeave}
     */
    onResourceDragLeave: EventEmitter<MbscResourceDragEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceRightClick}
     */
    onResourceRightClick: EventEmitter<MbscResourceClickEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onResourceOrderUpdate}
     */
    onResourceOrderUpdate: EventEmitter<MbscResourceOrderEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onSelectedDateChange}
     */
    onSelectedDateChange: EventEmitter<MbscSelectedDateChangeEvent>;
    /**
     * @event
     * {@inheritDoc MbscEventcalendarOptions.onVirtualLoading}
     */
    onVirtualLoading: EventEmitter<MbscVirtualLoadEvent>;
    /** @hidden */
    _hours: number[];
    /** @hidden */
    _els: MbscEventListService;
    /** @hidden */
    _instanceService: MbscInstanceService;
    /** @hidden */
    _getDayKey(index: number, data: MbscEventList): number;
    /** @hidden */
    _getEventKey(index: number, data: MbscCalendarEventData): string | number;
    protected _ctor(): void;
    protected _mounted(): void;
    protected _render(s: MbscEventcalendarOptions, state: MbscEventcalendarState): void;
    protected _updated(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscEventcalendar, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscEventcalendar, "mbsc-eventcalendar", never, { "actionableEvents": "actionableEvents"; "agendaEmptyTemplate": "agendaEmptyTemplate"; "agendaTemplate": "agendaTemplate"; "agendaEventTemplate": "agendaEventTemplate"; "agendaEventContentTemplate": "agendaEventContentTemplate"; "bufferAfterTemplate": "bufferAfterTemplate"; "bufferBeforeTemplate": "bufferBeforeTemplate"; "calendarEventContentTemplate": "calendarEventContentTemplate"; "calendarEventTemplate": "calendarEventTemplate"; "calendarSystem": "calendarSystem"; "cellTemplate": "cellTemplate"; "clickToCreate": "clickToCreate"; "colors": "colors"; "connections": "connections"; "data": "data"; "dayTemplate": "dayTemplate"; "agendaDayTemplate": "agendaDayTemplate"; "calendarDayTemplate": "calendarDayTemplate"; "schedulerDayTemplate": "schedulerDayTemplate"; "timelineDayTemplate": "timelineDayTemplate"; "dayContentTemplate": "dayContentTemplate"; "calendarDayContentTemplate": "calendarDayContentTemplate"; "schedulerDayContentTemplate": "schedulerDayContentTemplate"; "dayFooterTemplate": "dayFooterTemplate"; "dataTimezone": "dataTimezone"; "displayTimezone": "displayTimezone"; "dragBetweenResources": "dragBetweenResources"; "dragBetweenSlots": "dragBetweenSlots"; "dragInTime": "dragInTime"; "dragTimeStep": "dragTimeStep"; "dragTimeStepBase": "dragTimeStepBase"; "dragToCreate": "dragToCreate"; "dragToResize": "dragToResize"; "dragToMove": "dragToMove"; "eventContentTemplate": "eventContentTemplate"; "eventDelete": "eventDelete"; "eventOrder": "eventOrder"; "eventOverlap": "eventOverlap"; "eventTemplate": "eventTemplate"; "exclusiveEndDates": "exclusiveEndDates"; "extendDefaultEvent": "extendDefaultEvent"; "externalDrag": "externalDrag"; "externalDrop": "externalDrop"; "externalResourceDrag": "externalResourceDrag"; "externalResourceDrop": "externalResourceDrop"; "groupBy": "groupBy"; "headerTemplate": "headerTemplate"; "height": "height"; "hourTemplate": "hourTemplate"; "hourFooterTemplate": "hourFooterTemplate"; "immutableData": "immutableData"; "invalid": "invalid"; "invalidateEvent": "invalidateEvent"; "labelContentTemplate": "labelContentTemplate"; "labels": "labels"; "labelTemplate": "labelTemplate"; "marked": "marked"; "max": "max"; "min": "min"; "mousewheel": "mousewheel"; "monthTemplate": "monthTemplate"; "monthFooterTemplate": "monthFooterTemplate"; "popoverEventTemplate": "popoverEventTemplate"; "popoverEventContentTemplate": "popoverEventContentTemplate"; "refDate": "refDate"; "resources": "resources"; "resourceHeaderTemplate": "resourceHeaderTemplate"; "resourceFooterTemplate": "resourceFooterTemplate"; "resourceTemplate": "resourceTemplate"; "resourceEmptyTemplate": "resourceEmptyTemplate"; "slots": "slots"; "slotTemplate": "slotTemplate"; "scheduleEventContentTemplate": "scheduleEventContentTemplate"; "scheduleEventTemplate": "scheduleEventTemplate"; "schedulerEventContentTemplate": "schedulerEventContentTemplate"; "schedulerEventTemplate": "schedulerEventTemplate"; "selectedDate": "selectedDate"; "selectedEvents": "selectedEvents"; "selectMultipleEvents": "selectMultipleEvents"; "showControls": "showControls"; "showEventBuffer": "showEventBuffer"; "showEventTooltip": "showEventTooltip"; "sidebarFooterTemplate": "sidebarFooterTemplate"; "sidebarHeaderTemplate": "sidebarHeaderTemplate"; "sidebarTemplate": "sidebarTemplate"; "quarterTemplate": "quarterTemplate"; "quarterFooterTemplate": "quarterFooterTemplate"; "quarterText": "quarterText"; "timelineEventContentTemplate": "timelineEventContentTemplate"; "timelineEventTemplate": "timelineEventTemplate"; "valid": "valid"; "view": "view"; "width": "width"; "weekTemplate": "weekTemplate"; "weekFooterTemplate": "weekFooterTemplate"; "yearTemplate": "yearTemplate"; "yearFooterTemplate": "yearFooterTemplate"; "zoomLevel": "zoomLevel"; "allDayText": "allDayText"; "amText": "amText"; "dateFormat": "dateFormat"; "dateFormatFull": "dateFormatFull"; "dateFormatLong": "dateFormatLong"; "dayNames": "dayNames"; "dayNamesMin": "dayNamesMin"; "dayNamesShort": "dayNamesShort"; "eventsText": "eventsText"; "eventText": "eventText"; "firstDay": "firstDay"; "monthNames": "monthNames"; "monthNamesShort": "monthNamesShort"; "moreEventsPluralText": "moreEventsPluralText"; "moreEventsText": "moreEventsText"; "newEventText": "newEventText"; "nextPageText": "nextPageText"; "noEventsText": "noEventsText"; "pmText": "pmText"; "prevPageText": "prevPageText"; "timeFormat": "timeFormat"; "timezonePlugin": "timezonePlugin"; }, { "selectedDateChange": "selectedDateChange"; "selectedEventsChange": "selectedEventsChange"; "onCellClick": "onCellClick"; "onCellDoubleClick": "onCellDoubleClick"; "onCellRightClick": "onCellRightClick"; "onCellHoverIn": "onCellHoverIn"; "onCellHoverOut": "onCellHoverOut"; "onResourceHoverIn": "onResourceHoverIn"; "onResourceHoverOut": "onResourceHoverOut"; "onEventClick": "onEventClick"; "onEventDoubleClick": "onEventDoubleClick"; "onEventRightClick": "onEventRightClick"; "onEventHoverIn": "onEventHoverIn"; "onEventHoverOut": "onEventHoverOut"; "onEventCreate": "onEventCreate"; "onEventCreated": "onEventCreated"; "onEventDelete": "onEventDelete"; "onEventDeleted": "onEventDeleted"; "onEventDragEnd": "onEventDragEnd"; "onEventDragStart": "onEventDragStart"; "onEventUpdate": "onEventUpdate"; "onEventUpdated": "onEventUpdated"; "onEventCreateFailed": "onEventCreateFailed"; "onEventUpdateFailed": "onEventUpdateFailed"; "onEventDragEnter": "onEventDragEnter"; "onEventDragLeave": "onEventDragLeave"; "onLabelClick": "onLabelClick"; "onSelectedEventsChange": "onSelectedEventsChange"; "onPageChange": "onPageChange"; "onPageLoaded": "onPageLoaded"; "onPageLoading": "onPageLoading"; "onResourceClick": "onResourceClick"; "onResourceCollapse": "onResourceCollapse"; "onResourceDoubleClick": "onResourceDoubleClick"; "onResourceExpand": "onResourceExpand"; "onResourceDragStart": "onResourceDragStart"; "onResourceCreate": "onResourceCreate"; "onResourceCreated": "onResourceCreated"; "onResourceDelete": "onResourceDelete"; "onResourceDeleted": "onResourceDeleted"; "onResourceDragEnd": "onResourceDragEnd"; "onResourceDragEnter": "onResourceDragEnter"; "onResourceDragLeave": "onResourceDragLeave"; "onResourceRightClick": "onResourceRightClick"; "onResourceOrderUpdate": "onResourceOrderUpdate"; "onSelectedDateChange": "onSelectedDateChange"; "onVirtualLoading": "onVirtualLoading"; }, never, never>;
}
