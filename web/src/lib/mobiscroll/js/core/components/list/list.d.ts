import { BaseComponent, IBaseProps } from '../../base';
import * as i0 from "@angular/core";
export interface MbscListOptions extends IBaseProps {
    theme?: string;
}
/** @hidden */
export declare class ListBase extends BaseComponent<MbscListOptions, any> {
    _cssClass?: string;
    protected _render(s: MbscListOptions): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<ListBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<ListBase, "[mbsc-l-b]", never, {}, {}, never>;
}
