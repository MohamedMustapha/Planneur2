import { ElementRef } from '@angular/core';
import { CheckboxBase } from '../../core/components/checkbox/checkbox';
import * as i0 from "@angular/core";
export declare class MbscCheckbox extends CheckboxBase {
    vInput: ElementRef;
    color?: 'primary' | 'secondary' | 'success' | 'danger' | 'warning' | 'info' | 'dark' | 'light';
    defaultChecked?: boolean;
    description?: string;
    disabled?: boolean;
    inputStyle?: 'underline' | 'box' | 'outline';
    label?: string;
    name?: string;
    position?: 'start' | 'end';
    private _check?;
    get checked(): any;
    set checked(val: any);
    registerOnChange(fn: any): void;
    registerOnTouched(fn: any): void;
    setDisabledState(disabled: boolean): void;
    writeValue(val: any): void;
    _change(checked: boolean): void;
    protected _mounted(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscCheckbox, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscCheckbox, "mbsc-checkbox", never, { "color": "color"; "defaultChecked": "defaultChecked"; "description": "description"; "disabled": "disabled"; "inputStyle": "inputStyle"; "label": "label"; "name": "name"; "position": "position"; "checked": "checked"; }, {}, never, ["*"]>;
}
