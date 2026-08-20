import { InjectionToken } from '@angular/core';
import { MbscOptions } from '../core/commons';
import { Observable } from '../core/util/observable';
import * as i0 from "@angular/core";
export declare const MBSC_OPTIONS: InjectionToken<any>;
/**
 * A service for providing options for components, as well as means to be updated
 * when these options change.
 */
export declare class MbscOptionsService<OptionType extends MbscOptions = MbscOptions> {
    private _staticOptions;
    private _parentService;
    /**
     * Observable that notifies subscribers on the setting changes
     */
    change: Observable<OptionType>;
    private _options;
    constructor(_staticOptions: any, _parentService: MbscOptionsService<any>);
    /**
     * Returns the options the service provides
     */
    get options(): OptionType;
    /**
     * Provides the new options to the consumers of the service.
     * Notifies any subscribers for the change.
     * @param newOptions The new options object
     */
    setOptions(newOptions: OptionType): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscOptionsService<any>, [{ optional: true; }, { optional: true; skipSelf: true; }]>;
    static ɵprov: i0.ɵɵInjectableDeclaration<MbscOptionsService<any>>;
}
