import { BaseComponent } from '../../base';
import { Observable } from '../../util/observable';
import { MbscDraggableOptions } from './draggable.types.public';
import * as i0 from "@angular/core";
export declare const dragObservable: Observable<any>;
export interface MbscDraggableState {
    hasHover?: boolean;
    hasFocus?: boolean;
}
export declare function subscribeExternalDrag(handler: (value: any) => void): number;
export declare function unsubscribeExternalDrag(key: number): void;
export declare function moveClone(ev: any, clone: HTMLElement): void;
/** @hidden */
export declare class DraggableBase extends BaseComponent<MbscDraggableOptions, MbscDraggableState> {
    protected static _name: string;
    private _ctx?;
    private _dragData?;
    private _unlisten?;
    protected _render(s: MbscDraggableOptions): void;
    protected _updated(): void;
    protected _destroy(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<DraggableBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<DraggableBase, "[mbsc-drag]", never, {}, {}, never>;
}
