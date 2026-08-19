import { ITimeSlot, TimegridBase } from '../../core/components/timegrid/timegrid';
import * as i0 from "@angular/core";
export declare class MbscTimeBox {
    disabled?: boolean;
    selected?: boolean;
    timeSlot: ITimeSlot;
    theme?: string;
    onClick: (v: ITimeSlot) => void;
    onKeyDown: (event: any) => void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscTimeBox, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscTimeBox, "mbsc-timebox", never, { "disabled": "disabled"; "selected": "selected"; "timeSlot": "timeSlot"; "theme": "theme"; "onClick": "onClick"; "onKeyDown": "onKeyDown"; }, {}, never, never>;
}
export declare class MbscTimegrid extends TimegridBase {
    protected _mounted(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscTimegrid, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscTimegrid, "mbsc-timegrid", never, {}, {}, never, never>;
}
