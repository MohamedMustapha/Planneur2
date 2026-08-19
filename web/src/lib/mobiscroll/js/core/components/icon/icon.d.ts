import { BaseComponent } from '../../base';
import * as i0 from "@angular/core";
export interface MbscIconOptions {
    class?: string;
    className?: string;
    name?: string;
    svg?: string;
    theme?: string;
    onClick?: any;
}
/** @hidden */
export declare class IconBase extends BaseComponent<MbscIconOptions, any> {
    _cssClass?: string;
    _hasChildren?: boolean;
    _svg: any;
    protected _render(s: MbscIconOptions): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<IconBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<IconBase, "[mbsc-i-b]", never, {}, {}, never>;
}
