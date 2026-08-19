import { TemplateRef } from '@angular/core';
import { CalendarLabelBase } from '../../core/shared/calendar-view/calendar-label';
import { ICalendarProcessedLabel, MbscResource } from '../../core/shared/calendar-view/calendar-view.types';
import { MbscTimezonePlugin } from '../../core/util/datetime.types.public';
import * as i0 from "@angular/core";
/** @hidden */
export declare class MbscCalendarLabel extends CalendarLabelBase {
    amText?: string;
    contentTemplate?: TemplateRef<any>;
    count?: string;
    dataTimezone?: string;
    date?: number;
    displayTimezone?: string;
    drag?: boolean;
    hidden?: boolean;
    firstDay?: number;
    event?: any;
    editMode?: boolean;
    exclusiveEndDates?: boolean;
    inactive?: boolean;
    isActiveMonth?: boolean;
    isExact?: boolean;
    label?: ICalendarProcessedLabel;
    position?: {
        left?: string;
        right?: string;
        width: string;
    };
    resize?: boolean;
    hideLabel?: boolean;
    id?: any;
    isPicker?: boolean;
    isUpdate?: boolean;
    lastDay?: Date;
    more?: string;
    pmText?: string;
    resourcesMap?: {
        [key: number]: MbscResource;
    };
    selected?: boolean;
    showEventTooltip?: boolean;
    showText?: boolean;
    template?: TemplateRef<any>;
    timezonePlugin?: MbscTimezonePlugin;
    onClick?: (args: any, inst: any) => void;
    onDoubleClick?: (args: any, inst: any) => void;
    onRightClick?: (args: any, inst: any) => void;
    onHoverIn?: (args: any, inst: any) => void;
    onHoverOut?: (args: any, inst: any) => void;
    onDelete?: (args: any, inst: any) => void;
    onDragStart?: (args: any, inst: any) => void;
    onDragMove?: (args: any, inst: any) => void;
    onDragEnd?: (args: any, inst: any) => void;
    onDragModeOn?: (args: any, inst: any) => void;
    onDragModeOff?: (args: any, inst: any) => void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscCalendarLabel, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscCalendarLabel, "mbsc-calendar-label", never, { "amText": "amText"; "contentTemplate": "contentTemplate"; "count": "count"; "dataTimezone": "dataTimezone"; "date": "date"; "displayTimezone": "displayTimezone"; "drag": "drag"; "hidden": "hidden"; "firstDay": "firstDay"; "event": "event"; "editMode": "editMode"; "exclusiveEndDates": "exclusiveEndDates"; "inactive": "inactive"; "isActiveMonth": "isActiveMonth"; "isExact": "isExact"; "label": "label"; "position": "position"; "resize": "resize"; "hideLabel": "hideLabel"; "id": "id"; "isPicker": "isPicker"; "isUpdate": "isUpdate"; "lastDay": "lastDay"; "more": "more"; "pmText": "pmText"; "resourcesMap": "resourcesMap"; "selected": "selected"; "showEventTooltip": "showEventTooltip"; "showText": "showText"; "template": "template"; "timezonePlugin": "timezonePlugin"; "onClick": "onClick"; "onDoubleClick": "onDoubleClick"; "onRightClick": "onRightClick"; "onHoverIn": "onHoverIn"; "onHoverOut": "onHoverOut"; "onDelete": "onDelete"; "onDragStart": "onDragStart"; "onDragMove": "onDragMove"; "onDragEnd": "onDragEnd"; "onDragModeOn": "onDragModeOn"; "onDragModeOff": "onDragModeOff"; }, {}, never, never>;
}
