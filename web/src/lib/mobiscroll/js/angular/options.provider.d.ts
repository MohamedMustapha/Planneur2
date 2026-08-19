import { OnChanges, SimpleChanges } from '@angular/core';
import { MbscOptions } from '../core/commons';
import { MbscOptionsService } from './options.service';
import * as i0 from "@angular/core";
export declare class MbscOptionsProviderComponent implements OnChanges {
    private _opt;
    options?: MbscOptions;
    constructor(_opt: MbscOptionsService);
    ngOnChanges(changes: SimpleChanges): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscOptionsProviderComponent, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscOptionsProviderComponent, "mbsc-options-provider", never, { "options": "options"; }, {}, never, ["*"]>;
}
