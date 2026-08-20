import { DraggableBase } from '../../core/components/draggable/draggable';
import { MbscCalendarEvent } from '../../core/shared/calendar-view/calendar-view.types';
import * as i0 from "@angular/core";
/** @hidden */
export declare class DraggableDirective extends DraggableBase {
    dragData?: MbscCalendarEvent;
    type?: 'event' | 'resource';
    static ɵfac: i0.ɵɵFactoryDeclaration<DraggableDirective, never>;
    static ɵdir: i0.ɵɵDirectiveDeclaration<DraggableDirective, "[mbsc-draggable]", never, { "dragData": "dragData"; "type": "type"; }, {}, never>;
}
