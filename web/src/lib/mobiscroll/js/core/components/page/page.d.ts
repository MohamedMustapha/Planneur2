import { BaseComponent } from '../../base';
import { MbscPageOptions } from './page.types.public';
import * as i0 from "@angular/core";
/** @hidden */
export declare class PageBase extends BaseComponent<MbscPageOptions, any> {
    /** @hidden */
    static defaults: MbscPageOptions;
    protected static _name: string;
    _cssClass?: string;
    protected _render(s: MbscPageOptions): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<PageBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<PageBase, "[mbsc-page-b]", never, {}, {}, never>;
}
