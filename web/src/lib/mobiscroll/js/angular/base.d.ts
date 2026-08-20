import { AfterViewChecked, AfterViewInit, ChangeDetectorRef, Directive, DoCheck, ElementRef, EventEmitter, Injector, NgModule, NgZone, OnChanges, OnDestroy, OnInit, SimpleChanges, ViewContainerRef } from '@angular/core';
import { NgControl } from '@angular/forms';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { IModule } from '../core/base';
import { MbscOptions } from '../core/commons';
import { MbscOptionsService } from './options.service';
import * as i0 from "@angular/core";
export interface IDirectiveType extends Directive {
    standalone?: boolean;
}
export interface INgModuleType extends NgModule {
    entryComponents?: any[];
}
export declare function updateValue(inst: any, val: any): void;
/** @hidden */
export declare class Base<PropsType extends MbscOptions, StateType> implements AfterViewChecked, AfterViewInit, DoCheck, OnChanges, OnDestroy, OnInit {
    protected _cdr: ChangeDetectorRef;
    protected _elr: ElementRef;
    protected _zone: NgZone;
    protected _sanitizer: DomSanitizer;
    protected _injector: Injector;
    protected _vcf: ViewContainerRef;
    protected _ctrl: NgControl;
    protected _optp: MbscOptionsService<PropsType>;
    /** {@inheritDoc IBaseProps.cssClass} */
    class?: any;
    /** {@inheritDoc IBaseProps.context} */
    context?: any;
    /**
     * {@inheritDoc IBaseProps.locale}
     * @group Localizations
     */
    locale?: any;
    /** {@inheritDoc IBaseProps.modules} */
    modules?: IModule[];
    /** {@inheritDoc IBaseProps} */
    options?: PropsType;
    /** {@inheritDoc IBaseProps.responsive} */
    responsive?: {
        [key: string]: PropsType;
    };
    /**
     * {@inheritDoc IBaseProps.rtl}
     * @group Localizations
     */
    rtl?: boolean;
    /** {@inheritDoc IBaseProps.theme} */
    theme?: string;
    /** {@inheritDoc IBaseProps.themeVariant} */
    themeVariant?: 'light' | 'dark' | 'auto';
    /** {@inheritDoc IBaseProps.touchUi} */
    touchUi?: boolean | 'auto';
    /** @hidden */
    valueChange: EventEmitter<any>;
    /**
     * {@inheritDoc IBaseProps.onDestroy}
     * @event
     */
    onDestroy: EventEmitter<any>;
    /**
     *
     * {@inheritDoc IBaseProps.onInit}
     * @event
     */
    onInit: EventEmitter<any>;
    /** @hidden */
    state: StateType;
    /** @hidden */
    props: PropsType;
    /** @hidden */
    _el: any;
    /** @hidden */
    _shouldEnhance?: HTMLElement | string | boolean | null;
    /** @hidden */
    _val: any;
    /** @hidden */
    _onFormChange: (value: any) => any;
    /** @hidden */
    _onFormTouch: (ev?: any) => any;
    protected _opt?: PropsType;
    private _doCheck?;
    private _ctxChange;
    /** @hidden */
    constructor(_cdr: ChangeDetectorRef, _elr: ElementRef, _zone: NgZone, _sanitizer: DomSanitizer, _injector: Injector, _vcf: ViewContainerRef, _ctrl: NgControl, _optp: MbscOptionsService<PropsType>, _globals: any);
    /** @hidden */
    get value(): any;
    /** @hidden */
    set value(val: any);
    /** @hidden */
    ngOnChanges(changes: SimpleChanges): void;
    /** @hidden */
    ngDoCheck(): void;
    /** @hidden */
    ngAfterViewChecked(): void;
    /** @hidden */
    ngOnInit(): void;
    /** @hidden */
    ngAfterViewInit(): void;
    /** @hidden */
    ngOnDestroy(): void;
    /** @hidden */
    _getKey(index: number): number;
    /** @hidden */
    forceUpdate(): void;
    /** @hidden */
    setState(newState: any): void;
    protected _emit(name: string, args: any): void;
    protected _safeHtml(html: string): SafeHtml;
    protected _ctor(): void;
    protected _init(): void;
    protected _baseInit(): void;
    protected _mounted(): void;
    protected _updated(): void;
    protected _destroy(): void;
    protected _baseDestroy(): void;
    protected _willUpdate(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<Base<any, any>, [null, null, null, null, null, null, { optional: true; self: true; }, { optional: true; }, { optional: true; }]>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<Base<any, any>, "[mbsc-b]", never, { "class": "class"; "context": "context"; "locale": "locale"; "modules": "modules"; "options": "options"; "responsive": "responsive"; "rtl": "rtl"; "theme": "theme"; "themeVariant": "themeVariant"; "touchUi": "touchUi"; "value": "value"; }, { "valueChange": "valueChange"; "onDestroy": "onDestroy"; "onInit": "onInit"; }, never>;
}
