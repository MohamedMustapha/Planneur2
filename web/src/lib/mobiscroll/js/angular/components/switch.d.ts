import { ElementRef } from '@angular/core';
import { SwitchBase } from '../../core/components/switch/switch';
import * as i0 from "@angular/core";
export declare class MbscSwitch extends SwitchBase {
    vInput: ElementRef;
    vHandle: ElementRef;
    vHandleCont: ElementRef;
    color?: 'primary' | 'secondary' | 'success' | 'danger' | 'warning' | 'info' | 'dark' | 'light';
    description?: string;
    disabled?: boolean;
    id?: string;
    label?: string;
    position?: 'start' | 'end';
    readonly?: boolean;
    _id: string;
    private _check?;
    get checked(): any;
    set checked(val: any);
    registerOnChange(fn: any): void;
    registerOnTouched(fn: any): void;
    setDisabledState(disabled: boolean): void;
    writeValue(val: any): void;
    _change(checked: boolean): void;
    protected _mounted(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSwitch, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscSwitch, "mbsc-switch", never, { "color": "color"; "description": "description"; "disabled": "disabled"; "id": "id"; "label": "label"; "position": "position"; "readonly": "readonly"; "checked": "checked"; }, {}, never, ["*"]>;
}
