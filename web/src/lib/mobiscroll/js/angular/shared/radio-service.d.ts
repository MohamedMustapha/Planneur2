import * as i0 from "@angular/core";
export declare class MbscRadioService {
    color?: 'primary' | 'secondary' | 'success' | 'danger' | 'warning' | 'info' | 'dark' | 'light';
    disabled?: boolean;
    name: string;
    position?: 'start' | 'end';
    select?: 'single' | 'multiple';
    value?: any;
    onChange?: (ev: any, value: any) => void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscRadioService, never>;
    static ɵprov: i0.ɵɵInjectableDeclaration<MbscRadioService>;
}
