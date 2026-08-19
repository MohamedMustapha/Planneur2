import { ElementRef, EventEmitter, QueryList, TemplateRef } from '@angular/core';
import { DatepickerBase, MbscDatepickerState } from '../../core/components/datepicker/datepicker';
import { MbscDatepickerActiveDateChangeEvent, MbscDatepickerCancelEvent, MbscDatepickerCellClickEvent, MbscDatepickerChangeEvent, MbscDatepickerCloseEvent, MbscDatepickerLabelClickEvent, MbscDatepickerOpenEvent, MbscDatepickerOptions, MbscDatepickerPageChangeEvent, MbscDatepickerPageLoadedEvent, MbscDatepickerPageLoadingEvent, MbscDatepickerTempChangeEvent } from '../../core/components/datepicker/datepicker.types.public';
import { MbscPopupButton, MbscPopupDisplay } from '../../core/components/popup/popup.types.public';
import { MbscCalendarColor, MbscCalendarLabel, MbscCalendarMarked } from '../../core/shared/calendar-view/calendar-view.types';
import { MbscDateType, MbscTimezonePlugin } from '../../core/util/datetime.types.public';
import { MbscInput } from './input';
import { MbscPopup } from './popup';
import * as i0 from "@angular/core";
export declare class MbscDatepickerBase extends DatepickerBase {
    ariaLabel?: string;
    /**
     * Template reference for custom calendar header rendering.
     * @group Renderers
     */
    calendarHeaderTemplate?: TemplateRef<any>;
    calendarScroll?: 'horizontal' | 'vertical';
    calendarSize?: number;
    calendarSystem: any;
    calendarType?: 'year' | 'month' | 'week';
    colors?: MbscCalendarColor[];
    controls?: any[];
    dataTimezone?: string;
    defaultSelection: any;
    displayTimezone?: string;
    element: any;
    endInput: any;
    exclusiveEndDates?: boolean;
    firstSelectDay?: number;
    inputTyping?: boolean;
    invalid: any;
    inRangeInvalid?: boolean;
    labels?: MbscCalendarLabel[];
    marked?: MbscCalendarMarked[];
    max: any;
    maxRange?: number;
    maxTime: any;
    min: any;
    minRange?: number;
    minTime: any;
    mode?: 'date' | 'datetime' | 'time';
    moment: any;
    pages?: number | 'auto';
    rangeHighlight?: boolean;
    rangeEndInvalid?: boolean;
    refDate?: MbscDateType;
    returnFormat?: 'jsdate' | 'iso8601' | 'locale' | 'moment';
    showInput?: boolean;
    startInput: any;
    stepHour?: number;
    stepMinute?: number;
    stepSecond?: number;
    select?: 'date' | 'range' | 'preset-range';
    selectCounter?: boolean;
    selectSize?: number;
    selectMax?: number;
    selectMin?: number;
    selectMultiple?: boolean;
    showOuterDays?: boolean;
    showRangeLabels?: boolean;
    showWeekNumbers?: boolean;
    tabs?: boolean | 'auto';
    theme?: string;
    timezonePlugin?: MbscTimezonePlugin;
    valid?: any;
    weeks?: number;
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
    circular?: boolean | boolean[];
    clearText?: string;
    itemHeight?: number;
    maxWheelWidth?: number | number[];
    minWheelWidth?: number | number[];
    rows?: number;
    showOnClick?: boolean;
    showOnFocus?: boolean;
    wheelWidth?: number | number[];
    anchor?: any;
    anchorAlign?: 'start' | 'end' | 'center';
    animation?: 'pop' | 'slide-up' | 'slide-down' | boolean;
    buttons?: Array<MbscPopupButton | string>;
    cancelText?: string;
    closeOnEsc?: boolean;
    closeOnOverlayClick?: boolean;
    closeOnScroll?: boolean;
    closeText?: string;
    context?: any;
    display?: MbscPopupDisplay;
    focusOnClose?: boolean;
    focusTrap?: boolean;
    fullScreen?: boolean;
    headerText?: string;
    /**
     * Template reference for custom picker popup header rendering.
     * Takes priority over the [headerText](#opt-headerText) option.
     * @defaultValue undefined
     * @group Renderers
     */
    headerTemplate?: TemplateRef<any>;
    height?: number | string;
    isOpen?: boolean;
    maxHeight?: number | string;
    maxWidth?: number | string;
    scrollLock?: boolean;
    setText?: string;
    showArrow?: boolean;
    showOverlay?: boolean;
    width?: number | string;
    amText?: string;
    dateFormat?: string;
    dateWheels?: string;
    dayNames?: string[];
    dayNamesMin?: string[];
    dayNamesShort?: string[];
    dateText?: string;
    daySuffix?: string;
    dayText?: string;
    hourText?: string;
    monthText?: string;
    minuteText?: string;
    secondText?: string;
    separator?: string;
    timeText?: string;
    yearText?: string;
    /**
     * Template reference for customizing the day cells of the Datepicker.
     *
     * If you are looking to customize only the day cell content, you can use the
     * [dayContentTemplate](#template-dayContentTemplate) option.
     *
     * The following day specific details are available:
     * - `date`: _Date_ - The specific date as a Date object.
     * - `selected`: _boolean_ - True if the date is selected. (In case of calendar view)
     * - `events`: _Array<MbscCalendarEvent>_ - The list of events of the day.
     * @group Renderers
     */
    dayTemplate?: TemplateRef<any>;
    /**
     * Template reference for customizing the day cells of the Datepicker.
     *
     * If you are looking to fully customize the day cell (ex. add custom hover effects) you will need to use the
     * [dayTemplate](#template-dayTemplate) option.
     *
     * The following day specific details are available:
     * - `date`: _Date_ - The specific date as a Date object.
     * - `selected`: _boolean_ - True if the date is selected.
     * - `events`: _Array<MbscCalendarEvent>_ - The list of events of the day.
     * @group Renderers
     */
    dayContentTemplate?: TemplateRef<any>;
    firstDay?: number;
    monthNames?: string[];
    monthNamesShort?: string[];
    monthSuffix?: string;
    moreEventsPluralText?: string;
    moreEventsText?: string;
    nextPageText?: string;
    nowText?: string;
    pmText?: string;
    prevPageText?: string;
    rangeEndHelp?: string;
    rangeEndLabel?: string;
    rangeStartHelp?: string;
    rangeStartLabel?: string;
    selectedPluralText?: string;
    selectedText?: string;
    timeFormat?: string;
    timeWheels?: string;
    yearSuffix?: string;
    onCancel: EventEmitter<MbscDatepickerCancelEvent>;
    onCellClick: EventEmitter<MbscDatepickerCellClickEvent>;
    onCellHoverIn: EventEmitter<any>;
    onCellHoverOut: EventEmitter<any>;
    onChange: EventEmitter<MbscDatepickerChangeEvent>;
    onClose: EventEmitter<MbscDatepickerCloseEvent>;
    onLabelClick: EventEmitter<MbscDatepickerLabelClickEvent>;
    onPageChange: EventEmitter<MbscDatepickerPageChangeEvent>;
    onPageLoaded: EventEmitter<MbscDatepickerPageLoadedEvent>;
    onPageLoading: EventEmitter<MbscDatepickerPageLoadingEvent>;
    onOpen: EventEmitter<MbscDatepickerOpenEvent>;
    onPosition: EventEmitter<any>;
    onTempChange: EventEmitter<MbscDatepickerTempChangeEvent>;
    onActiveDateChange: EventEmitter<MbscDatepickerActiveDateChangeEvent>;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscDatepickerBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscDatepickerBase, "[mbsc-dp-b]", never, { "ariaLabel": "ariaLabel"; "calendarHeaderTemplate": "calendarHeaderTemplate"; "calendarScroll": "calendarScroll"; "calendarSize": "calendarSize"; "calendarSystem": "calendarSystem"; "calendarType": "calendarType"; "colors": "colors"; "controls": "controls"; "dataTimezone": "dataTimezone"; "defaultSelection": "defaultSelection"; "displayTimezone": "displayTimezone"; "element": "element"; "endInput": "endInput"; "exclusiveEndDates": "exclusiveEndDates"; "firstSelectDay": "firstSelectDay"; "inputTyping": "inputTyping"; "invalid": "invalid"; "inRangeInvalid": "inRangeInvalid"; "labels": "labels"; "marked": "marked"; "max": "max"; "maxRange": "maxRange"; "maxTime": "maxTime"; "min": "min"; "minRange": "minRange"; "minTime": "minTime"; "mode": "mode"; "moment": "moment"; "pages": "pages"; "rangeHighlight": "rangeHighlight"; "rangeEndInvalid": "rangeEndInvalid"; "refDate": "refDate"; "returnFormat": "returnFormat"; "showInput": "showInput"; "startInput": "startInput"; "stepHour": "stepHour"; "stepMinute": "stepMinute"; "stepSecond": "stepSecond"; "select": "select"; "selectCounter": "selectCounter"; "selectSize": "selectSize"; "selectMax": "selectMax"; "selectMin": "selectMin"; "selectMultiple": "selectMultiple"; "showOuterDays": "showOuterDays"; "showRangeLabels": "showRangeLabels"; "showWeekNumbers": "showWeekNumbers"; "tabs": "tabs"; "theme": "theme"; "timezonePlugin": "timezonePlugin"; "valid": "valid"; "weeks": "weeks"; "disabled": "disabled"; "dropdown": "dropdown"; "endIcon": "endIcon"; "endIconSrc": "endIconSrc"; "endIconSvg": "endIconSvg"; "error": "error"; "errorMessage": "errorMessage"; "inputStyle": "inputStyle"; "label": "label"; "labelStyle": "labelStyle"; "name": "name"; "placeholder": "placeholder"; "startIcon": "startIcon"; "startIconSrc": "startIconSrc"; "startIconSvg": "startIconSvg"; "tagInput": "tagInput"; "circular": "circular"; "clearText": "clearText"; "itemHeight": "itemHeight"; "maxWheelWidth": "maxWheelWidth"; "minWheelWidth": "minWheelWidth"; "rows": "rows"; "showOnClick": "showOnClick"; "showOnFocus": "showOnFocus"; "wheelWidth": "wheelWidth"; "anchor": "anchor"; "anchorAlign": "anchorAlign"; "animation": "animation"; "buttons": "buttons"; "cancelText": "cancelText"; "closeOnEsc": "closeOnEsc"; "closeOnOverlayClick": "closeOnOverlayClick"; "closeOnScroll": "closeOnScroll"; "closeText": "closeText"; "context": "context"; "display": "display"; "focusOnClose": "focusOnClose"; "focusTrap": "focusTrap"; "fullScreen": "fullScreen"; "headerText": "headerText"; "headerTemplate": "headerTemplate"; "height": "height"; "isOpen": "isOpen"; "maxHeight": "maxHeight"; "maxWidth": "maxWidth"; "scrollLock": "scrollLock"; "setText": "setText"; "showArrow": "showArrow"; "showOverlay": "showOverlay"; "width": "width"; "amText": "amText"; "dateFormat": "dateFormat"; "dateWheels": "dateWheels"; "dayNames": "dayNames"; "dayNamesMin": "dayNamesMin"; "dayNamesShort": "dayNamesShort"; "dateText": "dateText"; "daySuffix": "daySuffix"; "dayText": "dayText"; "hourText": "hourText"; "monthText": "monthText"; "minuteText": "minuteText"; "secondText": "secondText"; "separator": "separator"; "timeText": "timeText"; "yearText": "yearText"; "dayTemplate": "dayTemplate"; "dayContentTemplate": "dayContentTemplate"; "firstDay": "firstDay"; "monthNames": "monthNames"; "monthNamesShort": "monthNamesShort"; "monthSuffix": "monthSuffix"; "moreEventsPluralText": "moreEventsPluralText"; "moreEventsText": "moreEventsText"; "nextPageText": "nextPageText"; "nowText": "nowText"; "pmText": "pmText"; "prevPageText": "prevPageText"; "rangeEndHelp": "rangeEndHelp"; "rangeEndLabel": "rangeEndLabel"; "rangeStartHelp": "rangeStartHelp"; "rangeStartLabel": "rangeStartLabel"; "selectedPluralText": "selectedPluralText"; "selectedText": "selectedText"; "timeFormat": "timeFormat"; "timeWheels": "timeWheels"; "yearSuffix": "yearSuffix"; }, { "onCancel": "onCancel"; "onCellClick": "onCellClick"; "onCellHoverIn": "onCellHoverIn"; "onCellHoverOut": "onCellHoverOut"; "onChange": "onChange"; "onClose": "onClose"; "onLabelClick": "onLabelClick"; "onPageChange": "onPageChange"; "onPageLoaded": "onPageLoaded"; "onPageLoading": "onPageLoading"; "onOpen": "onOpen"; "onPosition": "onPosition"; "onTempChange": "onTempChange"; "onActiveDateChange": "onActiveDateChange"; }, never>;
}
/**
 * The Datepicker component.
 *
 * Usage:
 *
 * ```
 * <mbsc-datepicker></mbsc-datepicker>
 * ```
 */
export declare class MbscDatepicker extends MbscDatepickerBase {
    _controlRefs?: QueryList<any>;
    set vInput(inp: MbscInput);
    set vPopup(popup: MbscPopup);
    set vWrapper(wrapper: ElementRef);
    protected _render(s: MbscDatepickerOptions, state: MbscDatepickerState): void;
    protected _destroy(): void;
    protected _onClosed(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscDatepicker, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscDatepicker, "mbsc-datepicker", never, {}, {}, never, ["*"]>;
}
export declare class MbscDatepickerDirective extends MbscDatepickerBase {
    options: any;
    private _ref;
    protected _ctor(): void;
    protected _init(): void;
    protected _destroy(): void;
    protected _render(s: any, state: any): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscDatepickerDirective, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscDatepickerDirective, "[mbsc-datepicker]", ["mobiscroll"], { "options": "mbscOptions"; }, {}, never>;
}
