export interface IGestureArgs {
    moved: boolean;
    startX: number;
    startY: number;
    endX: number;
    endY: number;
    deltaX: number;
    deltaY: number;
    domEvent: Event;
    isTouch: boolean;
    target: HTMLElement;
}
export interface IGestureOptions {
    keepFocus: boolean;
    prevDef: boolean;
    onRelease(): void;
    onPress(): void;
    onStart(ev: IGestureArgs): any;
    onMove(ev: IGestureArgs): void;
    onEnd(ev: IGestureArgs): void;
    onHoverIn(ev: any): void;
    onHoverOut(ev: any): void;
    onKeyDown(ev: any): void;
    onFocus(ev: any, isFocusVisible?: boolean): void;
    onBlur(ev?: any): void;
    onChange(ev: any): void;
    onInput(ev: any): void;
    onDoubleClick(ev: IGestureArgs): void;
}
/** @hidden */
export declare function gestureListener(elm: HTMLElement, options: Partial<IGestureOptions>): () => void;
