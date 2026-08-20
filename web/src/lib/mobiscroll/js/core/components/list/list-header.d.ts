import { BaseComponent, IBaseProps } from '../../base';
import * as i0 from "@angular/core";
export interface MbscListHeaderOptions extends IBaseProps {
    theme?: string;
}
/** @hidden */
export declare class ListHeaderBase extends BaseComponent<MbscListHeaderOptions, any> {
    _cssClass?: string;
    protected _render(s: MbscListHeaderOptions): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<ListHeaderBase, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<ListHeaderBase, "[mbsc-lh-b]", never, {}, {}, never>;
}
