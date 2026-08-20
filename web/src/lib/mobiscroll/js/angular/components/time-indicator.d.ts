import { MbscSchedulerTimezone } from '../../core/components/eventcalendar/eventcalendar.types';
import { TimeIndicatorBase } from '../../core/components/eventcalendar/scheduler/time-indicator';
import { MbscTimezonePlugin } from '../../core/util/datetime.types.public';
import * as i0 from "@angular/core";
export declare class MbscTimeIndicator extends TimeIndicatorBase {
    amText?: string;
    displayedTime?: number;
    displayedDays?: number;
    displayTimezone?: string;
    endDay?: number;
    firstDay?: Date;
    hasResY?: boolean;
    orientation?: 'x' | 'y';
    pmText?: string;
    rtl?: boolean;
    showDayIndicator?: boolean;
    startDay?: number;
    startTime?: number;
    startTimeOffset?: number;
    theme?: string;
    timeFormat?: string;
    timezones?: MbscSchedulerTimezone[];
    timezonePlugin?: MbscTimezonePlugin;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscTimeIndicator, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscTimeIndicator, "mbsc-time-indicator", never, { "amText": "amText"; "displayedTime": "displayedTime"; "displayedDays": "displayedDays"; "displayTimezone": "displayTimezone"; "endDay": "endDay"; "firstDay": "firstDay"; "hasResY": "hasResY"; "orientation": "orientation"; "pmText": "pmText"; "rtl": "rtl"; "showDayIndicator": "showDayIndicator"; "startDay": "startDay"; "startTime": "startTime"; "startTimeOffset": "startTimeOffset"; "theme": "theme"; "timeFormat": "timeFormat"; "timezones": "timezones"; "timezonePlugin": "timezonePlugin"; }, {}, never, never>;
}
