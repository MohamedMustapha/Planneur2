import { ElementRef, EventEmitter } from '@angular/core';
import { StepperBase } from '../../core/components/stepper/stepper';
import * as i0 from "@angular/core";
export declare class MbscStepper extends StepperBase {
    vInput: ElementRef;
    color?: 'primary' | 'secondary' | 'success' | 'danger' | 'warning' | 'info' | 'dark' | 'light';
    defaultValue?: number;
    description?: string;
    disabled?: boolean;
    id?: string;
    inputPosition?: 'start' | 'end' | 'center';
    label?: string;
    min?: number;
    max?: number;
    name?: string;
    readonly?: boolean;
    step?: number;
    onChange: EventEmitter<any>;
    _id: string;
    _change(value: number): void;
    protected _mounted(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscStepper, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscStepper, "mbsc-stepper", never, { "color": "color"; "defaultValue": "defaultValue"; "description": "description"; "disabled": "disabled"; "id": "id"; "inputPosition": "inputPosition"; "label": "label"; "min": "min"; "max": "max"; "name": "name"; "readonly": "readonly"; "step": "step"; }, { "onChange": "onChange"; }, never, never>;
}
