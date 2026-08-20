import { AfterViewInit, ElementRef, EventEmitter, OnChanges, QueryList, SimpleChanges, TemplateRef } from '@angular/core';
import { MbscPopupButton, MbscPopupDisplay } from '../../core/components/popup/popup.types.public';
import { MbscScrollerWheel } from '../../core/components/scroller/scroller.types';
import { SelectBase } from '../../core/components/select/select';
import { MbscSelectCancelEvent, MbscSelectChangeEvent, MbscSelectCloseEvent, MbscSelectFilterEvent, MbscSelectOpenEvent, MbscSelectOptions, MbscSelectTempChangeEvent } from '../../core/components/select/select.types.public';
import { IPickerState } from '../../core/shared/picker/picker.types';
import { MbscInput } from './input';
import { MbscPopup } from './popup';
import * as i0 from "@angular/core";
export declare class MbscSelectOptionBase implements OnChanges, AfterViewInit {
    private _host;
    private _changeService;
    value?: any;
    group?: string;
    disabled?: boolean;
    text: string;
    protected _component: boolean;
    constructor(_host: ElementRef, _changeService: MbscSelectOptionChangeService);
    ngAfterViewInit(): void;
    ngOnChanges(changes: SimpleChanges): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSelectOptionBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscSelectOptionBase, "[mbsc-sel-b]", never, { "value": "value"; "group": "group"; "disabled": "disabled"; }, {}, never>;
}
export declare class MbscSelectOptionDirective extends MbscSelectOptionBase {
    value?: any;
    group?: string;
    disabled?: boolean;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSelectOptionDirective, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscSelectOptionDirective, "[mbsc-select-option]", never, { "value": "value"; "group": "group"; "disabled": "disabled"; }, {}, never>;
}
export declare class MbscSelectOptionComponent extends MbscSelectOptionBase {
    value?: any;
    group?: string;
    disabled?: boolean;
    protected _component: boolean;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSelectOptionComponent, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscSelectOptionComponent, "mbsc-select-option", never, { "value": "value"; "group": "group"; "disabled": "disabled"; }, {}, never, ["*"]>;
}
export declare class MbscSelectBase extends SelectBase {
    ariaLabel?: string;
    circular?: boolean | boolean[];
    clearIcon?: string;
    data?: any[];
    element?: any;
    defaultSelection?: any;
    filter?: boolean;
    formatValue?: (values: any[]) => string;
    getValue?: (values: any[]) => any;
    invalid?: any[];
    itemHeight?: number;
    /**
     * Template to customize each selectable item on the wheel.
     *
     * When the group wheel is also shown using the [`showGroupWheel`](#opt-showGroupWheel) option,
     * the `data` property and the `isGroup` property of the item will be `undefined` in the case of the group wheel items.
     * So, you can distinguish from the group wheel and the option wheel by checking if both the `data` and the `isGroup` are undefined.
     *
     * Available parameters:
     * - `display`: _string_ - The text of the item.
     * - `value`: _any_ - The value of the item.
     * - `isGroup`: _boolean_ - For group headers this property will be true
     * - `data`: _MbscSelectData_ - The original option item that is passed in the [`data`](#opt-data) array
     *
     * @defaultValue undefined
     *
     * @group Renderers
     */
    itemTemplate?: TemplateRef<any>;
    maxHeight?: string | number;
    maxWidth?: string | number;
    maxWheelWidth?: number | number[];
    minWheelWidth?: number | number[];
    parseValue?: (valueStr: string) => any[];
    rows?: number;
    rtl?: boolean;
    selectMultiple?: boolean;
    showGroupWheel?: boolean;
    showOnClick?: boolean;
    showOnFocus?: boolean;
    valueEquality?: (v1: any, v2: any) => boolean;
    wheels?: MbscScrollerWheel[][];
    wheelWidth?: number | number[];
    disabled?: boolean;
    dropdown?: boolean;
    endIcon?: string;
    endIconSrc?: string;
    endIconSvg?: string;
    error?: boolean;
    errorMessage?: string;
    inputStyle?: 'underline' | 'outline' | 'box';
    label?: string;
    labelStyle?: 'floating' | 'stacked' | 'inline';
    name?: string;
    placeholder?: string;
    startIcon?: string;
    startIconSrc?: string;
    startIconSvg?: string;
    tagInput?: boolean;
    anchor?: any;
    animation?: 'pop' | 'slide-up' | 'slide-down' | boolean;
    buttons?: Array<MbscPopupButton | string>;
    cancelText?: string;
    clearText?: string;
    closeOnEsc?: boolean;
    closeOnOverlayClick?: boolean;
    closeText?: string;
    context?: any;
    display?: MbscPopupDisplay;
    focusOnClose?: boolean;
    focusTrap?: boolean;
    headerText?: string;
    /**
     * Template reference for custom picker popup header rendering.
     * Takes priority over the [headerText](#opt-headerText) option.
     * @defaultValue undefined
     * @group Renderers
     */
    headerTemplate?: TemplateRef<any>;
    height?: number | string;
    setText?: string;
    showArrow?: boolean;
    showInput?: boolean;
    showOverlay?: boolean;
    width?: number | string;
    filterEmptyText?: string;
    filterPlaceholderText?: string;
    groupText?: string;
    onCancel: EventEmitter<MbscSelectCancelEvent>;
    onChange: EventEmitter<MbscSelectChangeEvent>;
    onClose: EventEmitter<MbscSelectCloseEvent>;
    onFilter: EventEmitter<MbscSelectFilterEvent>;
    onOpen: EventEmitter<MbscSelectOpenEvent>;
    onTempChange: EventEmitter<MbscSelectTempChangeEvent>;
    _defaultInput?: HTMLInputElement;
    protected _onValueChange(value: any): void;
    protected _render(s: MbscSelectOptions, state: IPickerState): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSelectBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscSelectBase, "[mbsc-sel-b]", never, { "ariaLabel": "ariaLabel"; "circular": "circular"; "clearIcon": "clearIcon"; "data": "data"; "element": "element"; "defaultSelection": "defaultSelection"; "filter": "filter"; "formatValue": "formatValue"; "getValue": "getValue"; "invalid": "invalid"; "itemHeight": "itemHeight"; "itemTemplate": "itemTemplate"; "maxHeight": "maxHeight"; "maxWidth": "maxWidth"; "maxWheelWidth": "maxWheelWidth"; "minWheelWidth": "minWheelWidth"; "parseValue": "parseValue"; "rows": "rows"; "rtl": "rtl"; "selectMultiple": "selectMultiple"; "showGroupWheel": "showGroupWheel"; "showOnClick": "showOnClick"; "showOnFocus": "showOnFocus"; "valueEquality": "valueEquality"; "wheels": "wheels"; "wheelWidth": "wheelWidth"; "disabled": "disabled"; "dropdown": "dropdown"; "endIcon": "endIcon"; "endIconSrc": "endIconSrc"; "endIconSvg": "endIconSvg"; "error": "error"; "errorMessage": "errorMessage"; "inputStyle": "inputStyle"; "label": "label"; "labelStyle": "labelStyle"; "name": "name"; "placeholder": "placeholder"; "startIcon": "startIcon"; "startIconSrc": "startIconSrc"; "startIconSvg": "startIconSvg"; "tagInput": "tagInput"; "anchor": "anchor"; "animation": "animation"; "buttons": "buttons"; "cancelText": "cancelText"; "clearText": "clearText"; "closeOnEsc": "closeOnEsc"; "closeOnOverlayClick": "closeOnOverlayClick"; "closeText": "closeText"; "context": "context"; "display": "display"; "focusOnClose": "focusOnClose"; "focusTrap": "focusTrap"; "headerText": "headerText"; "headerTemplate": "headerTemplate"; "height": "height"; "setText": "setText"; "showArrow": "showArrow"; "showInput": "showInput"; "showOverlay": "showOverlay"; "width": "width"; "filterEmptyText": "filterEmptyText"; "filterPlaceholderText": "filterPlaceholderText"; "groupText": "groupText"; }, { "onCancel": "onCancel"; "onChange": "onChange"; "onClose": "onClose"; "onFilter": "onFilter"; "onOpen": "onOpen"; "onTempChange": "onTempChange"; }, never>;
}
export declare class MbscSelectOptionChangeService {
    private _inst?;
    private _handler;
    reloadOptions(component: boolean): void;
    setInst(i: MbscSelectBase): void;
}
export declare class MbscSelect extends MbscSelectBase {
    optionComponents: QueryList<MbscSelectOptionComponent>;
    set vInput(inp: MbscInput);
    set vDefInput(inp: ElementRef);
    set vFilter(input: MbscInput);
    set vPopup(popup: MbscPopup);
    /** @hidden */
    updateOptionlist(): void;
    /** @hidden */
    valueSet(event: any): void;
    protected _ctor(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSelect, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscSelect, "mbsc-select", never, {}, {}, ["optionComponents"], ["*"]>;
}
export declare class MbscSelectDirective extends MbscSelectBase {
    options: any;
    inputElement: any;
    protected _inst?: SelectBase;
    private _ref;
    reloadOptionElements(): void;
    protected _ctor(): void;
    protected _init(): void;
    protected _destroy(): void;
    protected _render(s: MbscSelectOptions, state: IPickerState): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSelectDirective, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscSelectDirective, "[mbsc-select]", ["mobiscroll"], { "options": "mbscOptions"; "inputElement": "inputElement"; }, {}, never>;
}
