import { EventEmitter, TemplateRef } from '@angular/core';
import { MbscPopupDisplay } from '../../core/components/popup/popup.types.public';
import { ScrollerBase } from '../../core/components/scroller/scroller';
import { IWheelMoveArgs, MbscScrollerWheel } from '../../core/components/scroller/scroller.types';
import * as i0 from "@angular/core";
/**
 * The Scroller component.
 *
 * Usage:
 *
 * ```
 * <mbsc-scroller></mbsc-scroller>
 * ```
 */
export declare class MbscScroller extends ScrollerBase {
    circular?: boolean | boolean[];
    displayStyle?: MbscPopupDisplay;
    getValue?: (values: any[]) => any;
    maxWheelWidth?: number | number[];
    minWheelWidth?: number | number[];
    inContentTemplate: any;
    invalid?: any[];
    itemHeight?: number;
    itemTemplate?: TemplateRef<any>;
    onWheelMove?: (args: IWheelMoveArgs) => any;
    parseValue?: (valueStr: string) => any[];
    preContentTemplate: any;
    preContentData?: any;
    rows?: number;
    selectOnScroll?: boolean;
    shouldValidate?: (s: any, prevS: any) => boolean;
    validate?: (args: any) => any;
    valueEquality?: (v1: any, v2: any) => boolean;
    wheels?: MbscScrollerWheel[][];
    wheelWidth?: number | number[];
    display?: MbscPopupDisplay;
    valid?: any;
    onChange: EventEmitter<any>;
    onTempChange: EventEmitter<any>;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscScroller, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscScroller, "mbsc-scroller", never, { "circular": "circular"; "displayStyle": "displayStyle"; "getValue": "getValue"; "maxWheelWidth": "maxWheelWidth"; "minWheelWidth": "minWheelWidth"; "inContentTemplate": "inContentTemplate"; "invalid": "invalid"; "itemHeight": "itemHeight"; "itemTemplate": "itemTemplate"; "onWheelMove": "onWheelMove"; "parseValue": "parseValue"; "preContentTemplate": "preContentTemplate"; "preContentData": "preContentData"; "rows": "rows"; "selectOnScroll": "selectOnScroll"; "shouldValidate": "shouldValidate"; "validate": "validate"; "valueEquality": "valueEquality"; "wheels": "wheels"; "wheelWidth": "wheelWidth"; "display": "display"; "valid": "valid"; }, { "onChange": "onChange"; "onTempChange": "onTempChange"; }, never, never>;
}
