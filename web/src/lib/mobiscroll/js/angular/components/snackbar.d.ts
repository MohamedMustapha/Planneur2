import { MbscPopupOptions } from '../../core/components/popup/popup.types.public';
import { MbscPopup } from './popup';
import * as i0 from "@angular/core";
export declare class MbscSnackbar {
    popup: MbscPopup;
    button?: {
        action?: () => void;
        icon?: string;
        text?: string;
    };
    message?: string;
    props: MbscPopupOptions;
    onButtonClick(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscSnackbar, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscSnackbar, "mbsc-snackbar", never, { "button": "button"; "message": "message"; "props": "props"; }, {}, never, never>;
}
