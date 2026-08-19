import { BaseComponent } from '../../base';
import { MbscRadioGroupOptions } from './radio.types.public';
import * as i0 from "@angular/core";
/** @hidden */
export declare class RadioGroupBase extends BaseComponent<MbscRadioGroupOptions, any> {
    static defaults: MbscRadioGroupOptions;
    _groupClass?: string;
    _groupOpt: any;
    _name: string;
    private _id;
    _onChange: (ev: any, val: any) => void;
    _change(value: any): void;
    protected _render(s: MbscRadioGroupOptions): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<RadioGroupBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<RadioGroupBase, "[mbsc-rg-b]", never, {}, {}, never>;
}
