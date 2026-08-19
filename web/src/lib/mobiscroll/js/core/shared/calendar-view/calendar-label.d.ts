import { BaseComponent, IBaseProps } from '../../base';
import { MbscTimezonePlugin } from '../../util/datetime.types.public';
import { ICalendarProcessedLabel, MbscCalendarEventData, MbscCalendarLabel, MbscResource } from './calendar-view.types';
import * as i0 from "@angular/core";
/** @hidden */
export interface ICalendarLabelProps extends IBaseProps {
    amText?: string;
    count?: string;
    date?: number;
    dataTimezone?: string;
    displayTimezone?: string;
    drag?: boolean;
    event?: MbscCalendarLabel;
    exclusiveEndDates?: boolean;
    firstDay?: number;
    hidden?: boolean;
    id?: any;
    inactive?: boolean;
    isActiveMonth?: boolean;
    isExact?: boolean;
    isPicker?: boolean;
    isUpdate?: boolean;
    label: ICalendarProcessedLabel;
    lastDay?: Date;
    more?: string;
    position?: {
        left?: string;
        right?: string;
        width: string;
    };
    pmText?: string;
    resourcesMap?: {
        [key: string]: MbscResource;
    };
    rtl?: boolean;
    selected?: boolean;
    showEventTooltip?: boolean;
    showText?: boolean;
    theme?: string;
    timezonePlugin?: MbscTimezonePlugin;
    contentTemplate?: any;
    template?: any;
    resize?: boolean;
    width?: number;
    renderContent?(event: MbscCalendarEventData): any;
    render?(event: MbscCalendarEventData): any;
    onClick?(args: any): void;
    onDoubleClick?(args: any): void;
    onRightClick?(args: any): void;
    onHoverIn?(args: any): void;
    onHoverOut?(args: any): void;
    onDelete?(args: any, inst: any): void;
    onDragStart?(args: any): void;
    onDragMove?(args: any): void;
    onDragEnd?(args: any): void;
    onDragModeOn?(args: any): void;
    onDragModeOff?(args: any): void;
}
/** @hidden */
export interface ICalendarLabelState {
    hasFocus?: boolean;
    hasHover?: boolean;
}
/** @hidden */
export declare class CalendarLabelBase extends BaseComponent<ICalendarLabelProps, any> {
    _color?: string;
    _content?: any;
    _cssClass?: string;
    _data?: any;
    _hasResizeStart?: boolean;
    _hasResizeEnd?: boolean;
    _html?: any;
    _text?: string;
    _textColor?: string;
    _time?: string;
    _title?: string;
    _tabIndex?: number;
    private _doc?;
    private _isDrag?;
    private _touchTimer?;
    private _unlisten?;
    private _unsubscribe?;
    _onClick: (ev: any) => void;
    _onRightClick: (ev: any) => void;
    protected _onDocTouch: (ev: any) => void;
    protected _mounted(): void;
    protected _destroy(): void;
    protected _render(s: ICalendarLabelProps, state: ICalendarLabelState): void;
    private _updateState;
    private _triggerEvent;
    static ɵfac: i0.ɵɵFactoryDeclaration<CalendarLabelBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<CalendarLabelBase, "[mbsc-callbl-b]", never, {}, {}, never>;
}
