import { ElementRef, EventEmitter } from '@angular/core';
import { RadioBase } from '../../core/components/radio/radio';
import { MbscRadioService } from '../shared/radio-service';
import * as i0 from "@angular/core";
export declare class MbscRadio extends RadioBase {
    vInput: ElementRef;
    color?: string;
    description?: string;
    disabled?: boolean;
    id?: string;
    label?: string;
    name?: string;
    position?: 'start' | 'end';
    checkedChange: EventEmitter<any>;
    _radioService: MbscRadioService | null;
    private _check?;
    get checked(): any;
    set checked(val: any);
    registerOnChange(fn: any): void;
    registerOnTouched(fn: any): void;
    setDisabledState(disabled: boolean): void;
    writeValue(val: any): void;
    _change(checked: boolean): void;
    protected _ctor(): void;
    protected _mounted(): void;
    protected _render(s: any, state: any): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscRadio, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscRadio, "mbsc-radio", never, { "color": "color"; "description": "description"; "disabled": "disabled"; "id": "id"; "label": "label"; "name": "name"; "position": "position"; "checked": "checked"; }, { "checkedChange": "checkedChange"; }, never, ["*"]>;
}
