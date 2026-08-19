import { TemplateRef } from '@angular/core';
import { WeekDayBase } from '../../core/components/eventcalendar/scheduler/week-day';
import { MbscCalendarEvent } from '../../core/shared/calendar-view/calendar-view.types';
import * as i0 from "@angular/core";
export declare class MbscWeekDay extends WeekDayBase {
    background?: string;
    day?: number;
    dayNames: string[];
    dayTemplate?: TemplateRef<any>;
    dayContentTemplate?: TemplateRef<any>;
    events?: MbscCalendarEvent[];
    isToday?: boolean;
    label?: string;
    largeNames?: boolean;
    resource?: number | string;
    rtl?: boolean;
    selectable?: boolean;
    sticky?: boolean;
    selected?: boolean;
    theme?: string;
    timestamp?: number;
    onClick?: any;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscWeekDay, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscWeekDay, "mbsc-week-day", never, { "background": "background"; "day": "day"; "dayNames": "dayNames"; "dayTemplate": "dayTemplate"; "dayContentTemplate": "dayContentTemplate"; "events": "events"; "isToday": "isToday"; "label": "label"; "largeNames": "largeNames"; "resource": "resource"; "rtl": "rtl"; "selectable": "selectable"; "sticky": "sticky"; "selected": "selected"; "theme": "theme"; "timestamp": "timestamp"; "onClick": "onClick"; }, {}, never, never>;
}
