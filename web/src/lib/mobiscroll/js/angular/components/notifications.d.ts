import { ApplicationRef, Injector } from '@angular/core';
import { MbscAlertOptions, MbscConfirmOptions, MbscPromptOptions, MbscSnackbarOptions, MbscToastOptions } from '../../core/components/notifications/notifications.types';
import { MbscPrompt } from './prompt';
import { MbscSnackbar } from './snackbar';
import * as i0 from "@angular/core";
import * as i1 from "./input.module";
import * as i2 from "./popup.module";
export { MbscPrompt, MbscSnackbar };
export * from '../../core/components/notifications/notifications.types.public';
export declare class Notifications {
    private _injector;
    private _app;
    constructor(_injector: Injector, _app: ApplicationRef);
    toast(options: MbscToastOptions): Promise<undefined>;
    snackbar(options: MbscSnackbarOptions): Promise<undefined>;
    alert(options: MbscAlertOptions): Promise<undefined>;
    confirm(options: MbscConfirmOptions): Promise<boolean>;
    prompt(options: MbscPromptOptions): Promise<string | null>;
    private _createPopup;
    private _showToast;
    private _showSnackbar;
    private _showAlert;
    private _showConfirm;
    private _showPrompt;
    static ɵfac: i0.ɵɵFactoryDeclaration<Notifications, never>;
    static ɵprov: i0.ɵɵInjectableDeclaration<Notifications>;
}
export declare class MbscNotificationsModule {
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscNotificationsModule, never>;
    static ɵmod: i0.ɵɵNgModuleDeclaration<MbscNotificationsModule, never, [typeof i1.MbscInputModule, typeof i2.MbscPopupModule], never>;
    static ɵinj: i0.ɵɵInjectorDeclaration<MbscNotificationsModule>;
}
