import { EventEmitter } from '@angular/core';
import { DropcontainerBase } from '../../core/components/dropcontainer/dropcontainer';
import { MbscItemDragEvent } from '../../core/components/dropcontainer/dropcontainer.types.public';
import * as i0 from "@angular/core";
export declare class MbscDropcontainer extends DropcontainerBase {
    /** @event */
    onItemDragEnter: EventEmitter<MbscItemDragEvent>;
    /** @event */
    onItemDragLeave: EventEmitter<MbscItemDragEvent>;
    /** @event */
    onItemDrop: EventEmitter<MbscItemDragEvent>;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscDropcontainer, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscDropcontainer, "mbsc-dropcontainer", never, {}, { "onItemDragEnter": "onItemDragEnter"; "onItemDragLeave": "onItemDragLeave"; "onItemDrop": "onItemDrop"; }, never, ["*"]>;
}
