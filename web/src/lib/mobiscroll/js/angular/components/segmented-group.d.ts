import { MbscSegmentedGroupState, SegmentedGroupBase } from '../../core/components/segmented/segmented-group';
import { MbscSegmentedGroupOptions } from '../../core/components/segmented/segmented.types.public';
import { MbscRadioService } from '../shared/radio-service';
import * as i0 from "@angular/core";
export declare class MbscSegmentedGroup extends SegmentedGroupBase {
    color?: string;
    disabled?: string;
    name?: string;
    select?: 'single' | 'multiple';
    _radioService: MbscRadioService;
    protected _onValueChange(value: any): void;
    protected _ctor(): void;
    protected _render(s: MbscSegmentedGroupOptions, state: MbscSegmentedGroupState): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSegmentedGroup, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscSegmentedGroup, "mbsc-segmented-group", never, { "color": "color"; "disabled": "disabled"; "name": "name"; "select": "select"; }, {}, never, ["*"]>;
}
