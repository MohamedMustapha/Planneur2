import { PickerBase } from './picker';
export declare function getNativeElement(input: any, callback: (input: HTMLInputElement, hostEl?: HTMLElement) => void): void;
export declare function initPickerElement(el: HTMLElement, hostEl: HTMLElement | undefined, inst: PickerBase, handleChange?: (ev: any) => void, handleClick?: (ev: any) => void): () => void;
