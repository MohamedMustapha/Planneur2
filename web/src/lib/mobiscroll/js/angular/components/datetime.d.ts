import { EventEmitter } from '@angular/core';
import { DatetimeBase } from '../../core/components/datetime/datetime';
import * as i0 from "@angular/core";
export declare class MbscDateBase extends DatetimeBase {
    onChange: EventEmitter<any>;
    onTempChange: EventEmitter<any>;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscDateBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscDateBase, "[mbsc-dtb-b]", never, {}, { "onChange": "onChange"; "onTempChange": "onTempChange"; }, never>;
}
export declare class MbscDate extends MbscDateBase {
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscDate, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscDate, "mbsc-date", never, {}, {}, never, never>;
}
export declare class MbscDatetime extends MbscDateBase {
    protected _preset: string;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscDatetime, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscDatetime, "mbsc-datetime", never, {}, {}, never, never>;
}
export declare class MbscTime extends MbscDateBase {
    protected _preset: string;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscTime, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscTime, "mbsc-time", never, {}, {}, never, never>;
}
