import { MbscCalendarDayData } from '../../../shared/calendar-view/calendar-view.types';
import { STBase } from '../shared/schedule-timeline-base';
import { IDailyData, ISTOptions, ISTState } from '../shared/schedule-timeline-base.types';
import * as i0 from "@angular/core";
export interface ISchedulerOptions extends ISTOptions {
    groupBy?: 'date' | 'resource';
    type: 'month' | 'week' | 'day';
    renderDayContent?(args: MbscCalendarDayData): any;
    onWeekDayClick(arg: any): void;
}
export interface ISchedulerState extends ISTState {
    headerMonth?: number;
    headerYear?: number;
    showShadow?: boolean;
    virtualY?: number;
    virtualStartX?: number;
    virtualEndX?: number;
}
/** @hidden */
export declare class SchedulerBase extends STBase<ISchedulerOptions, ISchedulerState> {
    _colClass: string;
    _headerMonth: string;
    _headerYear: string;
    _largeDayNames: boolean;
    _timeWidth?: {
        width: string;
    };
    _virtualTimes: Array<{
        v: number;
        first: boolean;
        last: boolean;
        hidden: boolean;
        dayLimit: boolean;
    }>;
    _phXStart: number;
    _phXEnd: number;
    _phYStart: number;
    _phYEnd: number;
    _viewColors: {
        [key: string]: {
            [key: string]: IDailyData;
        };
    };
    _viewEvents: {
        [key: string]: {
            [key: string]: IDailyData;
        };
    };
    _viewInvalids: {
        [key: string]: {
            [key: string]: IDailyData;
        };
    };
    _virtualEnd: number;
    _virtualStart: number;
    private _loadTimer;
    private _oldResourceStart;
    private _oldResourceEnd;
    private _oldViewStart;
    private _oldViewEnd;
    private _oldTimeEnd;
    private _oldTimeStart;
    private _oldPageX;
    private _oldPageY;
    _onScroll: (ev?: any) => void;
    _setCont: (el: any) => void;
    _setTimeCont: (el: any) => void;
    _setAllDayCont: (el: any) => void;
    _setGridCont: (el: any) => void;
    _setHeaderCont: (el: any) => void;
    _setCursorTimeCont: (el: any) => void;
    protected _render(s: ISchedulerOptions, state: ISchedulerState): void;
    private _fillEmptyData;
    private _getVirtualData;
    static ɵfac: i0.ɵɵFactoryDeclaration<SchedulerBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<SchedulerBase, "[mbsc-sch-b]", never, {}, {}, never>;
}
