import { BaseComponent, IBaseProps } from '../../base';
import * as i0 from "@angular/core";
export interface MbscListItemOptions extends IBaseProps {
    actionable?: boolean;
    date?: number;
    drag?: boolean;
    event?: any;
    eventData?: any;
    ripple?: boolean;
    selected?: boolean;
    source?: 'agenda' | 'popover';
    onClick?(args: any): void;
    onDelete?(arg: any): void;
    onDoubleClick?(args: any): void;
    onRightClick?(args: any): void;
    onHoverIn?(args: any): void;
    onHoverOut?(args: any): void;
    onDragStart?(args: any): void;
    onDragMove?(args: any): void;
    onDragEnd?(args: any): void;
    onDragModeOn?(args: any): void;
    onDragModeOff?(args: any): void;
}
/** @hidden */
export interface MbscListItemState {
    hasFocus?: boolean;
    hasHover?: boolean;
    isActive?: boolean;
}
/** @hidden */
export declare class ListItemBase extends BaseComponent<MbscListItemOptions, MbscListItemState> {
    static defaults: MbscListItemOptions;
    protected static _name: string;
    _cssClass?: string;
    private _unlisten?;
    _onClick: (ev: any) => void;
    _onDoubleClick: (ev: any) => void;
    _onRightClick: (ev: any) => void;
    protected _mounted(): void;
    protected _render(s: MbscListItemOptions, state: MbscListItemState): void;
    protected _destroy(): void;
    private _triggerEvent;
    static ɵfac: i0.ɵɵFactoryDeclaration<ListItemBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<ListItemBase, "[mbsc-li-b]", never, {}, {}, never>;
}
