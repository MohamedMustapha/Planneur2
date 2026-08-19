import { EventEmitter } from '@angular/core';
import { CalendarBase } from '../../core/components/calendar/calendar';
import { ICalendarViewHost } from '../../core/shared/calendar-view/calendar-view.types';
import { MbscInstanceService } from '../shared/instance.service';
import * as i0 from "@angular/core";
/**
 * The Calendar component.
 *
 * Usage:
 *
 * ```
 * <mbsc-calendar></mbsc-calendar>
 * ```
 */
export declare class MbscCalendar extends CalendarBase implements ICalendarViewHost {
    onCancel: EventEmitter<any>;
    onChange: EventEmitter<any>;
    onClose: EventEmitter<any>;
    onOpen: EventEmitter<any>;
    _hours: number[];
    _instanceService: MbscInstanceService;
    set vCal(cal: any);
    protected _ctor(): void;
    protected _mounted(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscCalendar, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscCalendar, "mbsc-calendar", never, {}, { "onCancel": "onCancel"; "onChange": "onChange"; "onClose": "onClose"; "onOpen": "onOpen"; }, never, never>;
}
