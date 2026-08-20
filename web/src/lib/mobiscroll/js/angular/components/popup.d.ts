import { ElementRef, EventEmitter, TemplateRef } from '@angular/core';
import { PopupBase } from '../../core/components/popup/popup';
import { MbscPopupButton, MbscPopupCloseEvent, MbscPopupDisplay, MbscPopupOpenEvent, MbscPopupPositionEvent } from '../../core/components/popup/popup.types.public';
import * as i0 from "@angular/core";
export declare class MbscPopup extends PopupBase {
    vActive: ElementRef;
    vContent: ElementRef;
    vLimit: ElementRef;
    vWrapper: ElementRef;
    vPopup: ElementRef;
    activeElm: any;
    anchor: any;
    anchorAlign?: 'start' | 'end' | 'center';
    animation?: 'pop' | 'slide-down' | 'slide-up' | boolean;
    ariaLabel?: string;
    buttons?: Array<MbscPopupButton | 'ok' | 'close' | 'set' | 'cancel'>;
    buttonVariant?: 'standard' | 'flat' | 'outline';
    closeOnEsc?: boolean;
    closeOnOverlayClick?: boolean;
    closeOnScroll?: boolean;
    contentPadding?: boolean;
    cssClass?: string;
    disableLeftRight?: boolean;
    display?: MbscPopupDisplay;
    focusElm?: any;
    focusOnClose?: boolean;
    focusOnOpen?: boolean;
    focusTrap?: boolean;
    fullScreen?: boolean;
    /**
     * Template reference for custom popup header rendering.
     * Takes priority over the [headerText](#opt-headerText) option.
     *
     * @defaultValue undefined
     *
     * @group Renderers
     */
    headerTemplate?: TemplateRef<any>;
    headerText?: string;
    height?: string | number;
    isOpen?: boolean;
    maxHeight?: string | number;
    maxWidth?: string | number;
    scrollLock?: boolean;
    setActive?: boolean;
    showArrow?: boolean;
    showOverlay?: boolean;
    windowWidth?: number;
    width?: string | number;
    cancelText?: string;
    closeText?: string;
    okText?: string;
    setText?: string;
    onButtonClick: EventEmitter<any>;
    onOverlayClick: EventEmitter<any>;
    onClose: EventEmitter<MbscPopupCloseEvent>;
    onClosed: EventEmitter<any>;
    onKeyDown: EventEmitter<any>;
    onOpen: EventEmitter<MbscPopupOpenEvent>;
    onPosition: EventEmitter<MbscPopupPositionEvent>;
    onResize: EventEmitter<any>;
    /** Placeholder comment node */
    private _ph;
    protected _updated(): void;
    protected _onClosed(): void;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscPopup, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscPopup, "mbsc-popup", never, { "activeElm": "activeElm"; "anchor": "anchor"; "anchorAlign": "anchorAlign"; "animation": "animation"; "ariaLabel": "ariaLabel"; "buttons": "buttons"; "buttonVariant": "buttonVariant"; "closeOnEsc": "closeOnEsc"; "closeOnOverlayClick": "closeOnOverlayClick"; "closeOnScroll": "closeOnScroll"; "contentPadding": "contentPadding"; "cssClass": "cssClass"; "disableLeftRight": "disableLeftRight"; "display": "display"; "focusElm": "focusElm"; "focusOnClose": "focusOnClose"; "focusOnOpen": "focusOnOpen"; "focusTrap": "focusTrap"; "fullScreen": "fullScreen"; "headerTemplate": "headerTemplate"; "headerText": "headerText"; "height": "height"; "isOpen": "isOpen"; "maxHeight": "maxHeight"; "maxWidth": "maxWidth"; "scrollLock": "scrollLock"; "setActive": "setActive"; "showArrow": "showArrow"; "showOverlay": "showOverlay"; "windowWidth": "windowWidth"; "width": "width"; "cancelText": "cancelText"; "closeText": "closeText"; "okText": "okText"; "setText": "setText"; }, { "onButtonClick": "onButtonClick"; "onOverlayClick": "onOverlayClick"; "onClose": "onClose"; "onClosed": "onClosed"; "onKeyDown": "onKeyDown"; "onOpen": "onOpen"; "onPosition": "onPosition"; "onResize": "onResize"; }, never, ["*"]>;
}
