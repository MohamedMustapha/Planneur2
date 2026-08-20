import { TemplateRef, ViewContainerRef } from '@angular/core';
import * as i0 from "@angular/core";
export declare class MbscIfDirective {
    private _templateRef;
    private _viewContainerRef;
    static ngTemplateGuard_mbscIf: 'binding';
    private _context;
    private _viewRef;
    constructor(_templateRef: TemplateRef<MbscIfContext>, _viewContainerRef: ViewContainerRef);
    set mbscIf(condition: any);
    private _updateView;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscIfDirective, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<MbscIfDirective, "[mbscIf]", never, { "mbscIf": "mbscIf"; }, {}, never>;
}
declare class MbscIfContext {
    $implicit: any;
    mbscIf: any;
}
export {};
