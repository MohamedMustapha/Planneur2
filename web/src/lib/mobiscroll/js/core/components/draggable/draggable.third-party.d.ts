import { MbscDragulaDraggableOptions, MbscSortableJsDraggableOptions } from './draggable.types.public';
export declare const sortableJsDraggable: {
    /**
     * Initialize SortableJS draggable.
     *
     * @param instance - SortableJS instance.
     * @param options - The options object for the sortableJsDraggable.
     *
     */
    init: (instance: any, options: MbscSortableJsDraggableOptions) => void;
};
export declare const dragulaDraggable: {
    /**
     * Initialize Dragula draggable.
     *
     * @param instance - Dragula instance.
     * @param options - The options object for the dragulaDraggable.
     *
     */
    init: (instance: any, options?: MbscDragulaDraggableOptions) => void;
};
