import { RadioGroupBase } from '../../core/components/radio/radio-group';
import { MbscRadioService } from '../shared/radio-service';
import * as i0 from "@angular/core";
export declare class MbscRadioGroup extends RadioGroupBase {
    color?: string;
    disabled?: string;
    name?: string;
    position?: 'start' | 'end';
    _radioService: MbscRadioService;
    protected _onValueChange(value: any): void;
    protected _ctor(): void;
    protected _render(s: any): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscRadioGroup, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscRadioGroup, "mbsc-radio-group", never, { "color": "color"; "disabled": "disabled"; "name": "name"; "position": "position"; }, {}, never, ["*"]>;
}
