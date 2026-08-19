import { ChangeDetectorRef, OnDestroy } from '@angular/core';
import { CalendarViewBase } from '../../core/shared/calendar-view/calendar-view';
import { ICalendarViewHost } from '../../core/shared/calendar-view/calendar-view.types';
import { MbscInstanceService } from './instance.service';
import * as i0 from "@angular/core";
export declare class CalendarSubscriber implements OnDestroy {
    protected _changeDetector: ChangeDetectorRef;
    _cssClass: string;
    _hostInst?: ICalendarViewHost;
    inst?: CalendarViewBase;
    instanceService?: MbscInstanceService;
    private _changeHandler?;
    private _readyHandler?;
    constructor(instanceService: MbscInstanceService, _changeDetector: ChangeDetectorRef);
    get cssClass(): string;
    set calendar(inst: ICalendarViewHost);
    ngOnDestroy(): void;
    protected _setupServices(serv: MbscInstanceService): void;
    protected _cleanupService(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<CalendarSubscriber, [{ optional: true; }, null]>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<CalendarSubscriber, "mbsc-cal-s", never, { "_cssClass": "class"; "calendar": "calendar"; }, {}, never>;
}
export declare class MbscCalendarPrev extends CalendarSubscriber {
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscCalendarPrev, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscCalendarPrev, "mbsc-calendar-prev", never, {}, {}, never, never>;
}
export declare class MbscCalendarNext extends CalendarSubscriber {
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscCalendarNext, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscCalendarNext, "mbsc-calendar-next", never, {}, {}, never, never>;
}
export declare class MbscCalendarToday extends CalendarSubscriber {
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscCalendarToday, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscCalendarToday, "mbsc-calendar-today", never, {}, {}, never, never>;
}
export declare class MbscCalendarNav extends CalendarSubscriber {
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscCalendarNav, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscCalendarNav, "mbsc-calendar-nav", never, {}, {}, never, never>;
}
