import { EventEmitter } from '@angular/core';
import { ListItemBase } from '../../core/components/list/list-item';
import * as i0 from "@angular/core";
export declare class MbscListItem extends ListItemBase {
    actionable?: boolean;
    cssClass?: string;
    drag?: boolean;
    event?: any;
    onClick: EventEmitter<any>;
    onDoubleClick: EventEmitter<any>;
    onRightClick: EventEmitter<any>;
    onHoverIn: EventEmitter<any>;
    onHoverOut: EventEmitter<any>;
    onDragEnd: EventEmitter<any>;
    onDragModeOff: EventEmitter<any>;
    onDragModeOn: EventEmitter<any>;
    onDragMove: EventEmitter<any>;
    onDragStart: EventEmitter<any>;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscListItem, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscListItem, "mbsc-list-item", never, { "actionable": "actionable"; "cssClass": "cssClass"; "drag": "drag"; "event": "event"; }, { "onClick": "onClick"; "onDoubleClick": "onDoubleClick"; "onRightClick": "onRightClick"; "onHoverIn": "onHoverIn"; "onHoverOut": "onHoverOut"; "onDragEnd": "onDragEnd"; "onDragModeOff": "onDragModeOff"; "onDragModeOn": "onDragModeOn"; "onDragMove": "onDragMove"; "onDragStart": "onDragStart"; }, never, ["*"]>;
}
