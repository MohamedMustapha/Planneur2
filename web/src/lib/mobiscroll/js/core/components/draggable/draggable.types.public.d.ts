import { IBaseProps } from '../../base';
import { MbscResponsiveOptions } from '../../commons';
import { MbscCalendarEvent, MbscResource } from '../../shared/calendar-view/calendar-view.types';
export interface MbscDraggableOptions extends IBaseProps {
    /** Specify where the dragged element will be appended in the DOM. */
    context?: string | HTMLElement;
    /** The data of the dragged element. */
    dragData?: MbscCalendarEvent | MbscResource | string;
    /** The HTML element of the dragged item. */
    element?: HTMLElement | null;
    /** The type of the draggable element. */
    type?: 'event' | 'resource';
    /** @hidden */
    cssClass?: string;
    /** @hidden */
    responsive?: MbscResponsiveOptions;
    /** @hidden */
    theme?: string;
    /** @hidden */
    themeVariant?: 'light' | 'dark' | 'auto';
}
export interface MbscExternalDropEvent {
    container: HTMLElement;
    position: number;
    dragData: MbscCalendarEvent | MbscResource;
    afterElement?: HTMLElement;
}
export interface MbscSortableJsDraggableOptions {
    /** The selector of the SortableJS clone element, typically '.sortable-drag'. */
    cloneSelector: string;
    /** Build the resource or event object. Defaults to `data-drag-data` of the `el`. */
    dragData?: (el: HTMLElement) => MbscCalendarEvent | MbscResource;
    /** Enables drop from Eventcalendar to SortableJS list. */
    externalDrop?: boolean;
    /** Triggered on drop from Eventcalendar to SortableJS list. */
    onExternalDrop?: (args: MbscExternalDropEvent) => void;
    /** Creates an event or resource on the Eventcalendar. Defaults to 'event'. */
    type?: 'event' | 'resource';
}
export interface MbscDragulaDraggableOptions {
    /** Build the resource or event object. Defaults to `data-drag-data` of the `el`. */
    dragData?: (el: HTMLElement) => MbscCalendarEvent | MbscResource;
    /** Enables drop from Eventcalendar to Dragula list. */
    externalDrop?: boolean;
    /** Triggered on drop from Eventcalendar to Dragula list. */
    onExternalDrop?: (args: MbscExternalDropEvent) => void;
    /** Creates an event or resource on the Eventcalendar. Defaults to 'event'. */
    type?: 'event' | 'resource';
}
