import { DoCheck, TemplateRef, ViewContainerRef } from '@angular/core';
import * as i0 from "@angular/core";
interface MbscForContext<T> {
    $implicit: T;
    mbscForOf: T[];
    index: number;
    count: number;
    first: boolean;
    last: boolean;
    even: boolean;
    odd: boolean;
}
export declare class MbscForDirective<T = any> implements DoCheck {
    private _viewContainerRef;
    private _templateRef;
    private _mbscForOf;
    private _viewsByKey;
    private _trackByFn;
    private _dirty;
    constructor(_viewContainerRef: ViewContainerRef, _templateRef: TemplateRef<MbscForContext<T>>);
    set mbscForOf(iterable: Iterable<T> | null | undefined);
    set mbscForTrackBy(fn: ((index: number, item: T) => any) | null | undefined);
    ngDoCheck(): void;
    private _getKey;
    private _render;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscForDirective<any>, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscForDirective<any>, "[mbscFor][mbscForOf]", never, { "mbscForOf": "mbscForOf"; "mbscForTrackBy": "mbscForTrackBy"; }, {}, never>;
}
export {};
