import { MbscPopupOptions } from '../../core/components/popup/popup.types.public';
import { MbscPopup } from './popup';
import * as i0 from "@angular/core";
export declare class MbscPrompt {
    popup: MbscPopup;
    inputType?: string;
    label?: string;
    message?: string;
    placeholder?: string;
    title?: string;
    props: MbscPopupOptions;
    value: string;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscPrompt, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscPrompt, "mbsc-prompt", never, { "inputType": "inputType"; "label": "label"; "message": "message"; "placeholder": "placeholder"; "title": "title"; "props": "props"; "value": "value"; }, {}, never, never>;
}
